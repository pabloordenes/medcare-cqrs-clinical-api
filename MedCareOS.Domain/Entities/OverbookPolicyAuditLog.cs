namespace MedCareOS.Domain.Entities;

public class OverbookPolicyAuditLog
{
    public const int MaxReasonLength = 500;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ChangedByUserId { get; private set; }
    public bool PreviousAutoOverbookEnabled { get; private set; }
    public bool NewAutoOverbookEnabled { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public int EvaluatedCountAtChange { get; private set; }
    public decimal FalsePositiveRateAtChange { get; private set; }
    public bool GatePassed { get; private set; }
    public bool ForcedOverride { get; private set; }
    public DateTimeOffset ChangedAtUtc { get; private set; }

    private OverbookPolicyAuditLog() { } // ef

    public static OverbookPolicyAuditLog Create(
        Guid tenantId,
        Guid changedByUserId,
        bool previousAutoOverbookEnabled,
        bool newAutoOverbookEnabled,
        string reason,
        int evaluatedCountAtChange,
        decimal falsePositiveRateAtChange,
        bool gatePassed,
        bool forcedOverride,
        DateTimeOffset changedAtUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId no puede estar vacío.", nameof(tenantId));
        if (changedByUserId == Guid.Empty)
            throw new ArgumentException("ChangedByUserId no puede estar vacío.", nameof(changedByUserId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("El motivo del cambio de política es obligatorio.", nameof(reason));

        var trimmed = reason.Trim();
        if (trimmed.Length > MaxReasonLength)
            trimmed = trimmed[..MaxReasonLength];

        return new OverbookPolicyAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ChangedByUserId = changedByUserId,
            PreviousAutoOverbookEnabled = previousAutoOverbookEnabled,
            NewAutoOverbookEnabled = newAutoOverbookEnabled,
            Reason = trimmed,
            EvaluatedCountAtChange = Math.Max(0, evaluatedCountAtChange),
            FalsePositiveRateAtChange = Math.Round(falsePositiveRateAtChange, 4, MidpointRounding.AwayFromZero),
            GatePassed = gatePassed,
            ForcedOverride = forcedOverride,
            ChangedAtUtc = changedAtUtc
        };
    }
}
