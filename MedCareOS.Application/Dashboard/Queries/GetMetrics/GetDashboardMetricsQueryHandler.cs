using System.Globalization;
using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Dashboard.Queries.GetMetrics;

public class GetDashboardMetricsQueryHandler : IRequestHandler<GetDashboardMetricsQuery, DashboardAusentismoDto>
{
    private readonly IApplicationDbContext _dbContext;

    public GetDashboardMetricsQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DashboardAusentismoDto> Handle(GetDashboardMetricsQuery request, CancellationToken cancellationToken)
    {
        var citasRaw = await (
            from app in _dbContext.Appointments.AsNoTracking()
            join block in _dbContext.ScheduleBlocks.AsNoTracking() on app.ScheduleBlockId equals block.Id
            join staff in _dbContext.Staffs.AsNoTracking() on block.StaffId equals staff.Id
            join patient in _dbContext.Patients.AsNoTracking() on app.PatientId equals patient.Id
            select new
            {
                app.Id,
                app.Status,
                app.ActualAttendance,
                app.ScoringStatus,
                app.NlpStatus,
                NoShowRiskScore = app.EffectiveRiskScore ?? app.ModelRiskScore ?? app.NoShowRiskScore,
                RiskBand = app.EffectiveBand.HasValue ? app.EffectiveBand.Value.ToLabel() : app.RiskBand,
                app.CreatedAt,
                block.StartTime,
                Specialty = staff.Specialty ?? "Medicina General",
                DoctorName = staff.FirstName + " " + staff.LastName,
                PatientName = patient.FirstName + " " + patient.LastName,
                PatientRut = patient.Rut,
                FactorPrincipal = app.NlpStatus == NlpStatus.Completed && !string.IsNullOrWhiteSpace(app.NlpSummary)
                    ? app.NlpSummary
                    : "Evaluación clínica pendiente"
            }
        ).ToListAsync(cancellationToken);

        var rescatadas = await _dbContext.AiResolutionTasks
            .AsNoTracking()
            .CountAsync(t => t.Status == AiResolutionStatus.Approved || t.Status == AiResolutionStatus.Executed, cancellationToken);

        // 2. IN-MEMORY SLICING: Cálculos base rápidos
        int total = citasRaw.Count;
        int asistidas = citasRaw.Count(c => c.ActualAttendance == true || c.Status == AppointmentStatus.Finished);
        int inasistencias = citasRaw.Count(c => c.ActualAttendance == false || c.Status == AppointmentStatus.NoShow);
        int canceladas = citasRaw.Count(c => c.Status == AppointmentStatus.Cancelled);
        int pendientes = citasRaw.Count(c => c.Status == AppointmentStatus.Scheduled || c.Status == AppointmentStatus.Confirmed);

        decimal tasaAsistencia = total == 0 ? 0 : Math.Round((decimal)asistidas / total * 100, 1);
        decimal tasaAusentismo = total == 0 ? 0 : Math.Round((decimal)inasistencias / total * 100, 1);
        decimal impactoPerdida = inasistencias * 25000m;

        var kpis = new ResumenKPIsDto(total, tasaAsistencia, tasaAusentismo, -2.8m, rescatadas, inasistencias, impactoPerdida);

        // 3. PROYECCIONES DE LISTAS
        
        // A. Distribución (Gráfico de Dona)
        var distribucion = new List<DistribucionEstadoDto>
        {
            new("Asistió", asistidas, tasaAsistencia, "#10B981"),
            new("No Asistió", inasistencias, tasaAusentismo, "#EF4444"),
            new("Cancelada con Aviso", canceladas, total == 0 ? 0 : Math.Round((decimal)canceladas / total * 100, 1), "#3B82F6"),
            new("Confirmada Pendiente", pendientes, total == 0 ? 0 : Math.Round((decimal)pendientes / total * 100, 1), "#F59E0B")
        };

        // B. Por Especialidad (Barras Horizontales)
        var porEspecialidad = citasRaw
            .GroupBy(c => c.Specialty)
            .Select(g => {
                int gTotal = g.Count();
                int gAsist = g.Count(c => c.ActualAttendance == true || c.Status == AppointmentStatus.Finished);
                int gInasist = g.Count(c => c.ActualAttendance == false || c.Status == AppointmentStatus.NoShow);
                decimal gTasa = gTotal == 0 ? 0 : Math.Round((decimal)gInasist / gTotal * 100, 1);
                return new AusentismoEspecialidadDto(g.Key, gTotal, gAsist, gInasist, gTasa, "estable");
            })
            .OrderByDescending(x => x.TasaAusentismo)
            .ToList();

        // C. Por Horario (Franjas Críticas)
        var porHorario = citasRaw
            .GroupBy(c => {
                int h = c.StartTime.ToLocalTime().Hour;
                if (h >= 8 && h < 10) return "08:00 - 10:00";
                if (h >= 10 && h < 12) return "10:00 - 12:00";
                if (h >= 12 && h < 14) return "12:00 - 14:00";
                if (h >= 14 && h < 16) return "14:00 - 16:00";
                return "16:00 - 18:00";
            })
            .Select(g => {
                int gTotal = g.Count();
                int gInasist = g.Count(c => c.ActualAttendance == false || c.Status == AppointmentStatus.NoShow);
                decimal gTasa = gTotal == 0 ? 0 : Math.Round((decimal)gInasist / gTotal * 100, 1);
                return new AusentismoHorarioDto(g.Key, gTasa, gTotal);
            })
            .OrderBy(x => x.Franja)
            .ToList();

        // D. Tendencia Temporal (Por Meses en formato local)
        var culturaEsp = new CultureInfo("es-ES");
        var tendencia = citasRaw
            .GroupBy(c => c.StartTime.ToString("MMM", culturaEsp)) // Ej: "sep", "oct", "nov"
            .Select(g => {
                int gTotal = g.Count();
                int gAsist = g.Count(c => c.ActualAttendance == true || c.Status == AppointmentStatus.Finished);
                int gInasist = g.Count(c => c.ActualAttendance == false || c.Status == AppointmentStatus.NoShow);
                int gCanc = g.Count(c => c.Status == AppointmentStatus.Cancelled);
                decimal gTasa = gTotal == 0 ? 0 : Math.Round((decimal)gInasist / gTotal * 100, 1);
                return new TendenciaTemporalDto(g.Key.ToUpper(), gAsist, gInasist, gCanc, gTasa);
            })
            .ToList();

        // E. Alertas de Riesgo (La tabla de acción predictiva)
        var alertasRiesgo = citasRaw
            .Where(c => (c.Status == AppointmentStatus.Scheduled || c.Status == AppointmentStatus.Confirmed)
                     && c.StartTime > DateTime.UtcNow
                     && c.ScoringStatus == ScoringStatus.Scored
                     && c.NoShowRiskScore.HasValue
                     && (c.RiskBand == RiskThresholds.HighRiskBand || c.NoShowRiskScore.Value >= RiskThresholds.ModerateRiskMin))
            .OrderByDescending(c => c.NoShowRiskScore!.Value)
            .Take(10) // Top 10 pacientes con más riesgo
            .Select(c => new CitaRiesgoDto(
                c.Id,
                c.PatientName,
                c.PatientRut,
                c.Specialty,
                c.DoctorName,
                c.StartTime.ToString("yyyy-MM-dd"),
                c.StartTime.ToString("HH:mm"),
                Math.Round(c.NoShowRiskScore!.Value * 100, 1),
                c.RiskBand ?? "ALTO",
                c.FactorPrincipal,
                "Pendiente"
            ))
            .ToList();

        return new DashboardAusentismoDto(
            Kpis: kpis,
            Tendencia: tendencia,
            PorEspecialidad: porEspecialidad,
            Distribucion: distribucion,
            PorHorario: porHorario,
            Omro: new EfectividadOmroDto(94.2m, 71.5m, 320, 285), // Hardcode por ahora hasta tener modulo de mensajeria
            AlertasRiesgo: alertasRiesgo
        );
    }
}