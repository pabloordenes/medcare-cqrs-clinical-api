using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class AppointmentScheduledAction
{
    public const int MaxLockedByLength = 128;
    public const int MaxErrorLength = 1000;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public ScheduledActionKind Kind { get; private set; }
    public DateTimeOffset DueAt { get; private set; }
    public ScheduledActionStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public int MaxAttempts { get; private set; }
    public string? LockedBy { get; private set; }
    public DateTimeOffset? LockedAt { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public Guid? LockToken { get; private set; }
    public string? LastError { get; private set; }
    public Guid CorrelationId { get; private set; }
    public int PlanVersion { get; private set; }
    public string MetadataJson { get; private set; } = "{}";
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AppointmentScheduledAction() { } // ef

    public static AppointmentScheduledAction Create(
        Guid tenantId,
        Guid appointmentId,
        ScheduledActionKind kind,
        DateTimeOffset dueAt,
        Guid correlationId,
        int planVersion = 1,
        int maxAttempts = 5,
        DateTimeOffset? utcNow = null,
        string? metadataJson = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId no puede estar vacío.", nameof(tenantId));

        if (appointmentId == Guid.Empty)
            throw new ArgumentException("AppointmentId no puede estar vacío.", nameof(appointmentId));

        if (correlationId == Guid.Empty)
            throw new ArgumentException("CorrelationId no puede estar vacío.", nameof(correlationId));

        if (planVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(planVersion), planVersion, "PlanVersion debe ser >= 1.");

        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "MaxAttempts debe ser >= 1.");

        var now = utcNow ?? DateTimeOffset.UtcNow;

        return new AppointmentScheduledAction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AppointmentId = appointmentId,
            Kind = kind,
            DueAt = dueAt,
            Status = ScheduledActionStatus.Pending,
            Attempts = 0,
            MaxAttempts = maxAttempts,
            CorrelationId = correlationId,
            PlanVersion = planVersion,
            MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public bool IsOwnedBy(string workerId, Guid? lockToken = null) =>
        Status == ScheduledActionStatus.Processing &&
        string.Equals(LockedBy, workerId, StringComparison.Ordinal) &&
        (!lockToken.HasValue || LockToken == lockToken.Value);

    public void Claim(string workerId, DateTimeOffset utcNow, TimeSpan leaseDuration, Guid lockToken)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("WorkerId es obligatorio.", nameof(workerId));

        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, "LeaseDuration debe ser mayor a cero.");

        if (lockToken == Guid.Empty)
            throw new ArgumentException("LockToken no puede estar vacío.", nameof(lockToken));

        var isPending = Status == ScheduledActionStatus.Pending;
        var isExpiredProcessing = Status == ScheduledActionStatus.Processing &&
                                  LeaseUntil.HasValue &&
                                  LeaseUntil.Value <= utcNow;

        if (!isPending && !isExpiredProcessing)
        {
            throw new InvalidOperationException(
                $"No se puede reclamar la acción {Id} en estado '{Status}' con lease vigente.");
        }

        Status = ScheduledActionStatus.Processing;
        LockedBy = Truncate(workerId, MaxLockedByLength);
        LockedAt = utcNow;
        LeaseUntil = utcNow.Add(leaseDuration);
        LockToken = lockToken;
        Attempts++;
        UpdatedAt = utcNow;
    }

    public void MarkCompleted(DateTimeOffset utcNow, string? metadataJson = null)
    {
        if (Status != ScheduledActionStatus.Processing)
            throw new InvalidOperationException($"Solo acciones 'Processing' pueden completarse (actual: '{Status}').");

        Status = ScheduledActionStatus.Completed;
        CompletedAt = utcNow;
        ClearLease();
        LastError = null;
        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            MetadataJson = metadataJson.Trim();
        }
        UpdatedAt = utcNow;
    }

    public void MarkSkipped(string reason, DateTimeOffset utcNow)
    {
        if (Status is not ScheduledActionStatus.Pending and not ScheduledActionStatus.Processing)
            throw new InvalidOperationException($"No se puede omitir la acción {Id} en estado '{Status}'.");

        Status = ScheduledActionStatus.Skipped;
        CompletedAt = utcNow;
        ClearLease();
        LastError = string.IsNullOrWhiteSpace(reason) ? "Omitida." : Truncate(reason, MaxErrorLength);
        UpdatedAt = utcNow;
    }

    public void Cancel(string reason, DateTimeOffset utcNow)
    {
        if (Status == ScheduledActionStatus.Cancelled)
            return;

        if (Status is not ScheduledActionStatus.Pending and not ScheduledActionStatus.Processing)
            throw new InvalidOperationException($"No se puede cancelar la acción {Id} en estado '{Status}'.");

        Status = ScheduledActionStatus.Cancelled;
        ClearLease();
        LastError = string.IsNullOrWhiteSpace(reason) ? "Cancelada." : Truncate(reason, MaxErrorLength);
        UpdatedAt = utcNow;
    }

    public void ScheduleRetry(DateTimeOffset nextDueAt, string error, DateTimeOffset utcNow)
    {
        if (Status != ScheduledActionStatus.Processing)
            throw new InvalidOperationException($"Solo acciones 'Processing' pueden reprogramarse para reintento (actual: '{Status}').");

        if (Attempts >= MaxAttempts)
        {
            MarkFailed(error, utcNow);
            return;
        }

        Status = ScheduledActionStatus.Pending;
        DueAt = nextDueAt;
        ClearLease();
        LastError = string.IsNullOrWhiteSpace(error) ? "Error transitorio." : Truncate(error, MaxErrorLength);
        UpdatedAt = utcNow;
    }

    public void MarkFailed(string error, DateTimeOffset utcNow)
    {
        if (Status is not ScheduledActionStatus.Pending and not ScheduledActionStatus.Processing)
            throw new InvalidOperationException($"No se puede marcar como fallida la acción {Id} en estado '{Status}'.");

        Status = ScheduledActionStatus.Failed;
        CompletedAt = utcNow;
        ClearLease();
        LastError = string.IsNullOrWhiteSpace(error) ? "Error terminal." : Truncate(error, MaxErrorLength);
        UpdatedAt = utcNow;
    }

    private void ClearLease()
    {
        LockedBy = null;
        LockedAt = null;
        LeaseUntil = null;
        LockToken = null;
    }

    private static string Truncate(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
