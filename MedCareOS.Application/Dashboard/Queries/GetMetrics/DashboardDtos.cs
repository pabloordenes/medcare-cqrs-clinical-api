using MedCareOS.Application.Overbooking.Queries.GetOverbookMetrics;

namespace MedCareOS.Application.Dashboard.Queries.GetMetrics;

public record ResumenKPIsDto(
    int TotalCitas,
    decimal TasaAsistencia,
    decimal TasaAusentismo,
    decimal DeltaAusentismoVsMesAnterior,
    int CitasRescatadasOMRO,
    int HorasMedicasPerdidas,
    decimal ImpactoEstimadoPerdida
);

public record TendenciaTemporalDto(
    string Periodo,
    int Asistidas,
    int Inasistencias,
    int CanceladasAnticipadas,
    decimal TasaAusentismo
);

public record AusentismoEspecialidadDto(
    string Especialidad,
    int TotalCitas,
    int Asistencias,
    int Inasistencias,
    decimal TasaAusentismo,
    string Tendencia // "alza", "baja", "estable"
);

public record DistribucionEstadoDto(
    string Estado, // "Asistió", "No Asistió", "Cancelada con Aviso", "Confirmada Pendiente"
    int Cantidad,
    decimal Porcentaje,
    string Color
);

public record AusentismoHorarioDto(
    string Franja,
    decimal TasaAusentismo,
    int CitasTotales
);

public record EfectividadOmroDto(
    decimal ConRecordatorioAsistencia,
    decimal SinRecordatorioAsistencia,
    int RecordatoriosEnviadosMes,
    int ConfirmacionesRecibidas
);

public record CitaRiesgoDto(
    Guid Id,
    string Paciente,
    string Rut,
    string Especialidad,
    string Medico,
    string Fecha,
    string Hora,
    decimal ProbabilidadAusentismo,
    string NivelRiesgo, // "ALTO", "MEDIO", "BAJO"
    string FactorPrincipal,
    string EstadoRecordatorio // "Enviado", "Pendiente", "Confirmado"
);

// El Wrapper final que el Frontend consumirá de una sola vez
public record DashboardAusentismoDto(
    ResumenKPIsDto Kpis,
    List<TendenciaTemporalDto> Tendencia,
    List<AusentismoEspecialidadDto> PorEspecialidad,
    List<DistribucionEstadoDto> Distribucion,
    List<AusentismoHorarioDto> PorHorario,
    EfectividadOmroDto Omro,
    List<CitaRiesgoDto> AlertasRiesgo,
    OverbookMetricsDto? OverbookMetrics = null
);