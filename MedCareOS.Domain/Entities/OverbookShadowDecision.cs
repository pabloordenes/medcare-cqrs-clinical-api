namespace MedCareOS.Domain.Entities;

public class OverbookShadowDecision
{
    public const int MaxDecisionLength = 40;
    public const int MaxClinicalExclusionCodeLength = 40;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public Guid? ScheduleBlockId { get; private set; }
    public decimal EffectiveRiskScore { get; private set; }
    public decimal ThresholdUsed { get; private set; }
    public string Decision { get; private set; } = string.Empty;
    public bool WouldHaveOverbooked { get; private set; }
    public bool FinalNotificationDelivered { get; private set; }
    public bool HasClinicalExclusion { get; private set; }
    public string? ClinicalExclusionCode { get; private set; }
    public bool? OutcomeAttendance { get; private set; }
    public bool IsSynthetic { get; private set; }
    public DateTimeOffset EvaluatedAtUtc { get; private set; }
    public DateTimeOffset? OutcomeResolvedAtUtc { get; private set; }
    public Guid CorrelationId { get; private set; }

    public bool IsFalsePositive => WouldHaveOverbooked && OutcomeAttendance == true;

    private OverbookShadowDecision() { } // ef

    public static OverbookShadowDecision Create(
        Guid tenantId,
        Guid? appointmentId,
        Guid? scheduleBlockId,
        decimal effectiveRiskScore,
        decimal thresholdUsed,
        string decision,
        bool wouldHaveOverbooked,
        bool finalNotificationDelivered,
        bool hasClinicalExclusion,
        string? clinicalExclusionCode,
        bool isSynthetic,
        DateTimeOffset evaluatedAtUtc,
        Guid correlationId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId no puede estar vacío.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(decision))
            throw new ArgumentException("Decision es obligatorio.", nameof(decision));
        if (correlationId == Guid.Empty)
            throw new ArgumentException("CorrelationId no puede estar vacío.", nameof(correlationId));

        var trimmedDecision = decision.Trim();
        if (trimmedDecision.Length > MaxDecisionLength)
            trimmedDecision = trimmedDecision[..MaxDecisionLength];

        string? trimmedCode = null;
        if (!string.IsNullOrWhiteSpace(clinicalExclusionCode))
        {
            var c = clinicalExclusionCode.Trim();
            trimmedCode = c.Length <= MaxClinicalExclusionCodeLength ? c : c[..MaxClinicalExclusionCodeLength];
        }

        return new OverbookShadowDecision
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AppointmentId = appointmentId == Guid.Empty ? null : appointmentId,
            ScheduleBlockId = scheduleBlockId == Guid.Empty ? null : scheduleBlockId,
            EffectiveRiskScore = Math.Round(effectiveRiskScore, 4, MidpointRounding.AwayFromZero),
            ThresholdUsed = Math.Round(thresholdUsed, 4, MidpointRounding.AwayFromZero),
            Decision = trimmedDecision,
            WouldHaveOverbooked = wouldHaveOverbooked,
            FinalNotificationDelivered = finalNotificationDelivered,
            HasClinicalExclusion = hasClinicalExclusion,
            ClinicalExclusionCode = trimmedCode,
            OutcomeAttendance = null,
            IsSynthetic = isSynthetic,
            EvaluatedAtUtc = evaluatedAtUtc,
            OutcomeResolvedAtUtc = null,
            CorrelationId = correlationId
        };
    }

    public void ResolveOutcome(bool attended, DateTimeOffset utcNow)
    {
        OutcomeAttendance = attended;
        OutcomeResolvedAtUtc ??= utcNow;
    }
}
