using MedCareOS.Application.Interfaces;
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
        var citasQuery = _dbContext.Appointments.AsNoTracking();

        var total = await citasQuery.CountAsync(cancellationToken);
        
        // Buscamos citas que tengan asistencia confirmada booleana o estado finalizado
        var asistidas = await citasQuery.CountAsync(
            c => c.ActualAttendance == true || c.Status == AppointmentStatus.Finished, 
            cancellationToken);
        
        // Buscamos citas que tengan inasistencia booleana o estado NoShow
        var inasistencias = await citasQuery.CountAsync(
            c => c.ActualAttendance == false || c.Status == AppointmentStatus.NoShow, 
            cancellationToken);
        
        // Buscamos tareas de OMRO aprobadas
        var rescatadas = await _dbContext.AiResolutionTasks
            .AsNoTracking()
            .CountAsync(t => t.Status == "Approved", cancellationToken);

        // Protección contra división por cero y redondeo
        decimal tasaAsistencia = total == 0 ? 0 : Math.Round((decimal)asistidas / total * 100, 1);
        decimal tasaAusentismo = total == 0 ? 0 : Math.Round((decimal)inasistencias / total * 100, 1);
        
        // Costo oportunidad: 25,000 CLP por cita perdida
        decimal impactoPerdida = inasistencias * 25000m; 

        var kpis = new ResumenKPIsDto(
            TotalCitas: total,
            TasaAsistencia: tasaAsistencia,
            TasaAusentismo: tasaAusentismo,
            DeltaAusentismoVsMesAnterior: -2.8m,
            CitasRescatadasOMRO: rescatadas,
            HorasMedicasPerdidas: inasistencias, // 1 hora de box por inasistencia
            ImpactoEstimadoPerdida: impactoPerdida
        );

        return new DashboardAusentismoDto(
            Kpis: kpis,
            Tendencia: new List<TendenciaTemporalDto>(),
            PorEspecialidad: new List<AusentismoEspecialidadDto>(),
            Distribucion: new List<DistribucionEstadoDto>(),
            PorHorario: new List<AusentismoHorarioDto>(),
            Omro: new EfectividadOmroDto(94.2m, 71.5m, 320, 285), 
            AlertasRiesgo: new List<CitaRiesgoDto>()
        );
    }
}