using System.Globalization;
using System.Text;
using MedCareOS.Application.Interfaces;
using MedCareOS.Application.Overbooking.Queries.GetOverbookMetrics;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Dashboard.Queries.GetMetrics;

public class GetDashboardMetricsQueryHandler : IRequestHandler<GetDashboardMetricsQuery, DashboardAusentismoDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IOverbookShadowDecisionRepository? _shadowDecisionRepository;

    public GetDashboardMetricsQueryHandler(
        IApplicationDbContext dbContext,
        IOverbookShadowDecisionRepository? shadowDecisionRepository = null)
    {
        _dbContext = dbContext;
        _shadowDecisionRepository = shadowDecisionRepository;
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

        // 2. Clasificación mutuamente excluyente (cada cita cae en un único bucket)
        var clasificadas = citasRaw.Select(c => new
        {
            Cita = c,
            Bucket = ClassifyBucket(c.Status, c.ActualAttendance),
            SpecialtyKey = NormalizeKey(c.Specialty)
        }).ToList();

        int total = clasificadas.Count;
        int asistidas = clasificadas.Count(x => x.Bucket == DashboardBucket.Attended);
        int inasistencias = clasificadas.Count(x => x.Bucket == DashboardBucket.NoShow);
        int canceladas = clasificadas.Count(x => x.Bucket == DashboardBucket.Cancelled);
        int pendientes = clasificadas.Count(x => x.Bucket == DashboardBucket.Pending);

        decimal tasaAsistencia = total == 0 ? 0 : Math.Round((decimal)asistidas / total * 100, 1);
        decimal tasaAusentismo = total == 0 ? 0 : Math.Round((decimal)inasistencias / total * 100, 1);
        decimal tasaCanceladas = total == 0 ? 0 : Math.Round((decimal)canceladas / total * 100, 1);
        decimal tasaPendientes = total == 0 ? 0 : Math.Round((decimal)pendientes / total * 100, 1);
        decimal impactoPerdida = inasistencias * 25000m;

        var kpis = new ResumenKPIsDto(total, tasaAsistencia, tasaAusentismo, -2.8m, rescatadas, inasistencias, impactoPerdida);

        // 3. PROYECCIONES DE LISTAS

        // A. Distribución (Gráfico de Dona — siempre suma ~100%)
        var distribucion = new List<DistribucionEstadoDto>
        {
            new("Asistió", asistidas, tasaAsistencia, "#10B981"),
            new("No Asistió", inasistencias, tasaAusentismo, "#EF4444"),
            new("Cancelada con Aviso", canceladas, tasaCanceladas, "#3B82F6"),
            new("Confirmada Pendiente", pendientes, tasaPendientes, "#F59E0B")
        };

        // B. Por Especialidad (agrupado sin tildes ni mayúsculas, mostrando la etiqueta mejor formateada)
        var porEspecialidad = clasificadas
            .GroupBy(x => x.SpecialtyKey)
            .Select(g =>
            {
                var displayLabel = g
                    .Select(x => x.Cita.Specialty.Trim())
                    .OrderByDescending(HasDiacritics)
                    .ThenByDescending(s => s.Length)
                    .First();

                int gTotal = g.Count();
                int gAsist = g.Count(x => x.Bucket == DashboardBucket.Attended);
                int gInasist = g.Count(x => x.Bucket == DashboardBucket.NoShow);
                decimal gTasa = gTotal == 0 ? 0 : Math.Round((decimal)gInasist / gTotal * 100, 1);
                return new AusentismoEspecialidadDto(displayLabel, gTotal, gAsist, gInasist, gTasa, "estable");
            })
            .OrderByDescending(x => x.TasaAusentismo)
            .ToList();

        // C. Por Horario (Franjas Críticas)
        var porHorario = clasificadas
            .GroupBy(x =>
            {
                int h = x.Cita.StartTime.ToLocalTime().Hour;
                if (h >= 8 && h < 10) return "08:00 - 10:00";
                if (h >= 10 && h < 12) return "10:00 - 12:00";
                if (h >= 12 && h < 14) return "12:00 - 14:00";
                if (h >= 14 && h < 16) return "14:00 - 16:00";
                return "16:00 - 18:00";
            })
            .Select(g =>
            {
                int gTotal = g.Count();
                int gInasist = g.Count(x => x.Bucket == DashboardBucket.NoShow);
                decimal gTasa = gTotal == 0 ? 0 : Math.Round((decimal)gInasist / gTotal * 100, 1);
                return new AusentismoHorarioDto(g.Key, gTasa, gTotal);
            })
            .OrderBy(x => x.Franja)
            .ToList();

        // D. Tendencia Temporal (ordenada cronológicamente: OCT -> NOV)
        var culturaEsp = new CultureInfo("es-ES");
        var tendencia = clasificadas
            .GroupBy(x => new { x.Cita.StartTime.Year, x.Cita.StartTime.Month })
            .OrderBy(g => g.Key.Year)
            .ThenBy(g => g.Key.Month)
            .Select(g =>
            {
                var label = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM", culturaEsp).ToUpperInvariant();
                int gTotal = g.Count();
                int gAsist = g.Count(x => x.Bucket == DashboardBucket.Attended);
                int gInasist = g.Count(x => x.Bucket == DashboardBucket.NoShow);
                int gCanc = g.Count(x => x.Bucket == DashboardBucket.Cancelled);
                decimal gTasa = gTotal == 0 ? 0 : Math.Round((decimal)gInasist / gTotal * 100, 1);
                return new TendenciaTemporalDto(label, gAsist, gInasist, gCanc, gTasa);
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

        OverbookMetricsDto? overbookMetrics = null;
        if (_shadowDecisionRepository is not null)
        {
            var metricsHandler = new GetOverbookMetricsQueryHandler(_dbContext, _shadowDecisionRepository);
            overbookMetrics = await metricsHandler.Handle(new GetOverbookMetricsQuery(RiskThresholds.DefaultTenantId), cancellationToken);
        }

        return new DashboardAusentismoDto(
            Kpis: kpis,
            Tendencia: tendencia,
            PorEspecialidad: porEspecialidad,
            Distribucion: distribucion,
            PorHorario: porHorario,
            Omro: new EfectividadOmroDto(94.2m, 71.5m, 320, 285), // Hardcode por ahora hasta tener modulo de mensajeria
            AlertasRiesgo: alertasRiesgo,
            OverbookMetrics: overbookMetrics
        );
    }
    
    private enum DashboardBucket
    {
        Attended,
        NoShow,
        Cancelled,
        Pending
    }

    private static DashboardBucket ClassifyBucket(AppointmentStatus status, bool? actualAttendance) => status switch
    {
        AppointmentStatus.Cancelled => DashboardBucket.Cancelled,
        AppointmentStatus.NoShow => DashboardBucket.NoShow,
        AppointmentStatus.Finished or AppointmentStatus.InProgress => DashboardBucket.Attended,
        _ when actualAttendance == true => DashboardBucket.Attended,
        _ when actualAttendance == false => DashboardBucket.NoShow,
        _ => DashboardBucket.Pending
    };

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "sin_especialidad";

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool HasDiacritics(string value)
        => value.Normalize(NormalizationForm.FormD)
            .Any(ch => CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark);
}