using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class WaitingListOffer
{
    public const int MaxSourceTypeLength = 32;
    public const int MaxCancellationReasonLength = 300;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid WaitingListId { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid ScheduleBlockId { get; private set; }
    public Guid? SourceAppointmentId { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public int CascadeAttemptNumber { get; private set; }
    public WaitingListOfferStatus Status { get; private set; }
    public DateTimeOffset OfferedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public Guid? ResultAppointmentId { get; private set; }
    public Guid? NotificationId { get; private set; }
    public string? CancellationReason { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private WaitingListOffer() { } // ef

    public static WaitingListOffer Create(
        Guid tenantId,
        Guid waitingListId,
        Guid patientId,
        Guid scheduleBlockId,
        string sourceType,
        Guid? sourceAppointmentId,
        int cascadeAttemptNumber,
        DateTimeOffset offeredAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId no puede estar vacío.", nameof(tenantId));
        if (waitingListId == Guid.Empty)
            throw new ArgumentException("WaitingListId no puede estar vacío.", nameof(waitingListId));
        if (patientId == Guid.Empty)
            throw new ArgumentException("PatientId no puede estar vacío.", nameof(patientId));
        if (scheduleBlockId == Guid.Empty)
            throw new ArgumentException("ScheduleBlockId no puede estar vacío.", nameof(scheduleBlockId));
        if (string.IsNullOrWhiteSpace(sourceType))
            throw new ArgumentException("SourceType es obligatorio.", nameof(sourceType));
        if (cascadeAttemptNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(cascadeAttemptNumber), "CascadeAttemptNumber debe ser >= 1.");
        if (expiresAtUtc <= offeredAtUtc)
            throw new ArgumentException("ExpiresAt debe ser posterior a OfferedAt.", nameof(expiresAtUtc));

        var trimmedSourceType = sourceType.Trim();
        if (trimmedSourceType.Length > MaxSourceTypeLength)
            trimmedSourceType = trimmedSourceType[..MaxSourceTypeLength];

        var id = Guid.NewGuid();
        return new WaitingListOffer
        {
            Id = id,
            TenantId = tenantId,
            WaitingListId = waitingListId,
            PatientId = patientId,
            ScheduleBlockId = scheduleBlockId,
            SourceAppointmentId = sourceAppointmentId == Guid.Empty ? null : sourceAppointmentId,
            SourceType = trimmedSourceType,
            CascadeAttemptNumber = cascadeAttemptNumber,
            Status = WaitingListOfferStatus.Pending,
            OfferedAt = offeredAtUtc,
            ExpiresAt = expiresAtUtc,
            ConcurrencyStamp = Guid.NewGuid(),
            UpdatedAt = offeredAtUtc
        };
    }

    public void AttachNotification(Guid notificationId, DateTimeOffset utcNow)
    {
        if (notificationId == Guid.Empty)
            throw new ArgumentException("NotificationId no puede estar vacío.", nameof(notificationId));

        NotificationId = notificationId;
        UpdatedAt = utcNow;
    }

    public void Accept(Guid resultAppointmentId, DateTimeOffset utcNow)
    {
        if (resultAppointmentId == Guid.Empty)
            throw new ArgumentException("ResultAppointmentId no puede estar vacío.", nameof(resultAppointmentId));

        if (Status != WaitingListOfferStatus.Pending)
            throw new InvalidOperationException($"Solo se puede aceptar una oferta 'Pending' (actual: '{Status}').");

        if (ExpiresAt <= utcNow)
            throw new InvalidOperationException("La oferta de lista de espera ya expiró.");

        Status = WaitingListOfferStatus.Accepted;
        RespondedAt = utcNow;
        AcceptedAt = utcNow;
        ResultAppointmentId = resultAppointmentId;
        ConcurrencyStamp = Guid.NewGuid();
        UpdatedAt = utcNow;
    }

    public void Decline(string? reason, DateTimeOffset utcNow)
    {
        if (Status != WaitingListOfferStatus.Pending)
            throw new InvalidOperationException($"Solo se puede declinar una oferta 'Pending' (actual: '{Status}').");

        Status = WaitingListOfferStatus.Declined;
        RespondedAt = utcNow;
        CancellationReason = TruncateReason(reason ?? "DeclinedByCandidate");
        ConcurrencyStamp = Guid.NewGuid();
        UpdatedAt = utcNow;
    }

    public void Expire(DateTimeOffset utcNow)
    {
        if (Status != WaitingListOfferStatus.Pending)
            throw new InvalidOperationException($"Solo se puede expirar una oferta 'Pending' (actual: '{Status}').");

        Status = WaitingListOfferStatus.Expired;
        CancellationReason = "OfferExpired";
        ConcurrencyStamp = Guid.NewGuid();
        UpdatedAt = utcNow;
    }

    public void CancelByOriginalConfirmation(string reason, DateTimeOffset utcNow)
    {
        if (Status != WaitingListOfferStatus.Pending)
            throw new InvalidOperationException($"Solo se puede cancelar una oferta 'Pending' (actual: '{Status}').");

        Status = WaitingListOfferStatus.CancelledByOriginalConfirmation;
        CancellationReason = TruncateReason(reason);
        ConcurrencyStamp = Guid.NewGuid();
        UpdatedAt = utcNow;
    }

    public void Supersede(string reason, DateTimeOffset utcNow)
    {
        if (Status != WaitingListOfferStatus.Pending)
            throw new InvalidOperationException($"Solo se puede anular una oferta 'Pending' (actual: '{Status}').");

        Status = WaitingListOfferStatus.Superseded;
        CancellationReason = TruncateReason(reason);
        ConcurrencyStamp = Guid.NewGuid();
        UpdatedAt = utcNow;
    }

    private static string? TruncateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return null;

        var trimmed = reason.Trim();
        return trimmed.Length <= MaxCancellationReasonLength
            ? trimmed
            : trimmed[..MaxCancellationReasonLength];
    }
}
