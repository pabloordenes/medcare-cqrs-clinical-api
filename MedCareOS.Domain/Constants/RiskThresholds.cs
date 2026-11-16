namespace MedCareOS.Domain.Constants;

/// <summary>
/// Umbrales unificados de riesgo de inasistencia (No-Show):
/// - [0.00, 0.50): Riesgo bajo (sin intervención de OMRO).
/// - [0.50, 0.85): Riesgo moderado (propuesta HITL vía OMRO).
/// - [0.85, 1.00]: Riesgo crítico (sobrecupo autónomo vía OMRO).
/// </summary>
public static class RiskThresholds
{
    public const decimal ModerateRiskMin = 0.50m;
    public const decimal CriticalOverbookMin = 0.85m;
    public const string HighRiskBand = "Alto";
    public static readonly Guid DefaultTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public const string AppointmentNoShowAnomalyType = "AppointmentNoShowRisk";
}
