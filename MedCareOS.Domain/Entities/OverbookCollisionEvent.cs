using MedCareOS.Domain.Services;

namespace MedCareOS.Domain.Entities;

public class OverbookCollisionEvent
{
    public const int MaxTriggerSourceLength = 40;
    public const int MaxRuleAppliedLength = 80;
    public const int MaxOverrideReasonLength = 500;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OriginalAppointmentId { get; private set; }
    public Guid? OverbookAppointmentId { get; private set; }
    public Guid OverbookBlockId { get; private set; }
    public string TriggerSource { get; private set; } = string.Empty;
    public OverbookWinner Winner { get; private set; }
    public Guid? WinnerAppointmentId { get; private set; }
    public Guid? DisplacedAppointmentId { get; private set; }
    public string RuleApplied { get; private set; } = string.Empty;
    public bool IsManualOverride { get; private set; }
    public string? OverrideReason { get; private set; }
    public Guid? OverriddenByUserId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    private OverbookCollisionEvent() { } // ef

    public static OverbookCollisionEvent CreateCollision(
        Guid tenantId,
        Guid originalAppointmentId,
        Guid? overbookAppointmentId,
        Guid overbookBlockId,
        string triggerSource,
        OverbookPriorityDecision decision,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return new OverbookCollisionEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OriginalAppointmentId = originalAppointmentId,
            OverbookAppointmentId = overbookAppointmentId,
            OverbookBlockId = overbookBlockId,
            TriggerSource = Truncate(triggerSource, MaxTriggerSourceLength),
            Winner = decision.Winner,
            WinnerAppointmentId = decision.WinnerAppointmentId,
            DisplacedAppointmentId = decision.DisplacedAppointmentId,
            RuleApplied = Truncate(decision.RuleApplied, MaxRuleAppliedLength),
            IsManualOverride = false,
            OccurredAtUtc = utcNow
        };
    }

    public static OverbookCollisionEvent CreateUndo(
        Guid tenantId,
        Guid originalAppointmentId,
        Guid overbookBlockId,
        string triggerSource,
        DateTimeOffset utcNow)
    {
        return new OverbookCollisionEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OriginalAppointmentId = originalAppointmentId,
            OverbookAppointmentId = null,
            OverbookBlockId = overbookBlockId,
            TriggerSource = Truncate(triggerSource, MaxTriggerSourceLength),
            Winner = OverbookWinner.Original,
            WinnerAppointmentId = originalAppointmentId,
            DisplacedAppointmentId = null,
            RuleApplied = "OVERBOOK_UNDO_DEACTIVATED",
            IsManualOverride = false,
            OccurredAtUtc = utcNow
        };
    }

    public static OverbookCollisionEvent CreateManualOverride(
        Guid tenantId,
        Guid originalAppointmentId,
        Guid overbookAppointmentId,
        Guid overbookBlockId,
        OverbookWinner winner,
        Guid winnerAppointmentId,
        Guid displacedAppointmentId,
        string reason,
        Guid overriddenByUserId,
        DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("El motivo del override es obligatorio.", nameof(reason));

        if (overriddenByUserId == Guid.Empty)
            throw new ArgumentException("El usuario que ejecuta el override es obligatorio.", nameof(overriddenByUserId));

        return new OverbookCollisionEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OriginalAppointmentId = originalAppointmentId,
            OverbookAppointmentId = overbookAppointmentId,
            OverbookBlockId = overbookBlockId,
            TriggerSource = "ManualOverride",
            Winner = winner,
            WinnerAppointmentId = winnerAppointmentId,
            DisplacedAppointmentId = displacedAppointmentId,
            RuleApplied = "RECEPTION_MANUAL_OVERRIDE",
            IsManualOverride = true,
            OverrideReason = Truncate(reason, MaxOverrideReasonLength),
            OverriddenByUserId = overriddenByUserId,
            OccurredAtUtc = utcNow
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
