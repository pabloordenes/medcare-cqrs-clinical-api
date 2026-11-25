using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class RiskSignal
{
    public const int MaxSourceLength = 100;
    public const int MaxIdempotencyKeyLength = 200;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public RiskSignalKind Kind { get; private set; }
    public decimal Weight { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid CorrelationId { get; private set; }
    public string MetadataJson { get; private set; } = "{}";
    public DateTimeOffset CreatedAt { get; private set; }

    private RiskSignal() { } // ef

    public static RiskSignal Create(
        Guid tenantId,
        Guid appointmentId,
        RiskSignalKind kind,
        decimal weight,
        string source,
        string idempotencyKey,
        DateTimeOffset occurredAt,
        Guid correlationId,
        string? metadataJson = null,
        DateTimeOffset? createdAt = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId no puede estar vacío.", nameof(tenantId));

        if (appointmentId == Guid.Empty)
            throw new ArgumentException("AppointmentId no puede estar vacío.", nameof(appointmentId));

        if (correlationId == Guid.Empty)
            throw new ArgumentException("CorrelationId no puede estar vacío.", nameof(correlationId));

        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source es obligatorio.", nameof(source));

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("IdempotencyKey es obligatorio.", nameof(idempotencyKey));

        var normalizedSource = source.Trim();
        if (normalizedSource.Length > MaxSourceLength)
            normalizedSource = normalizedSource[..MaxSourceLength];

        var normalizedKey = idempotencyKey.Trim();
        if (normalizedKey.Length > MaxIdempotencyKeyLength)
            normalizedKey = normalizedKey[..MaxIdempotencyKeyLength];

        return new RiskSignal
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AppointmentId = appointmentId,
            Kind = kind,
            Weight = Math.Round(weight, 4, MidpointRounding.AwayFromZero),
            Source = normalizedSource,
            IdempotencyKey = normalizedKey,
            OccurredAt = occurredAt,
            CorrelationId = correlationId,
            MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson.Trim(),
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };
    }
}
