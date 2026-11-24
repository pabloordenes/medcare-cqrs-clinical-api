using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class Notification
{
    public const int MaxTemplateKeyLength = 100;
    public const int MaxLocaleLength = 10;
    public const int MaxProviderNameLength = 50;
    public const int MaxProviderMessageIdLength = 200;
    public const int MaxErrorLength = 1000;
    public const string RedactedPlaceholder = "[REDACTED]";

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public Guid PatientId { get; private set; }
    public NotificationType Type { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public NotificationChannel? OriginalChannel { get; private set; }
    public NotificationStatus Status { get; private set; }
    public string TemplateKey { get; private set; } = string.Empty;
    public int TemplateVersion { get; private set; }
    public string Locale { get; private set; } = "es-CL";
    public string TemplateParamsJson { get; private set; } = "{}";
    public string? Content { get; private set; }
    public DateTimeOffset? ContentRedactedAt { get; private set; }
    public DateTimeOffset ScheduledFor { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? ProviderName { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? Error { get; private set; }
    public Guid CorrelationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Notification() { } // ef

    public static Notification Create(
        Guid tenantId,
        Guid? appointmentId,
        Guid patientId,
        NotificationType type,
        NotificationChannel channel,
        string templateKey,
        int templateVersion,
        string locale,
        string templateParamsJson,
        DateTimeOffset scheduledFor,
        Guid correlationId,
        DateTimeOffset utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId no puede estar vacío.", nameof(tenantId));

        if (patientId == Guid.Empty)
            throw new ArgumentException("PatientId no puede estar vacío.", nameof(patientId));

        if (appointmentId.HasValue && appointmentId.Value == Guid.Empty)
            throw new ArgumentException("AppointmentId no puede ser Guid.Empty.", nameof(appointmentId));

        if (string.IsNullOrWhiteSpace(templateKey))
            throw new ArgumentException("TemplateKey es obligatorio.", nameof(templateKey));

        if (templateVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(templateVersion), "TemplateVersion debe ser >= 1.");

        if (string.IsNullOrWhiteSpace(locale))
            throw new ArgumentException("Locale es obligatorio.", nameof(locale));

        if (correlationId == Guid.Empty)
            throw new ArgumentException("CorrelationId no puede estar vacío.", nameof(correlationId));

        var initialStatus = scheduledFor > utcNow
            ? NotificationStatus.Scheduled
            : NotificationStatus.Pending;

        return new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AppointmentId = appointmentId,
            PatientId = patientId,
            Type = type,
            Channel = channel,
            Status = initialStatus,
            TemplateKey = templateKey.Trim(),
            TemplateVersion = templateVersion,
            Locale = locale.Trim(),
            TemplateParamsJson = string.IsNullOrWhiteSpace(templateParamsJson) ? "{}" : templateParamsJson,
            ScheduledFor = scheduledFor,
            Attempts = 0,
            CorrelationId = correlationId,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void MarkSent(string providerName, string providerMessageId, string sanitizedContent, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("ProviderName es obligatorio.", nameof(providerName));

        if (string.IsNullOrWhiteSpace(providerMessageId))
            throw new ArgumentException("ProviderMessageId es obligatorio.", nameof(providerMessageId));

        if (Status is NotificationStatus.Cancelled or NotificationStatus.Delivered or NotificationStatus.Read)
            throw new InvalidOperationException($"No se puede marcar como enviada una notificación en estado '{Status}'.");

        Attempts++;
        Status = NotificationStatus.Sent;
        SentAt ??= utcNow;
        ProviderName = Truncate(providerName, MaxProviderNameLength);
        ProviderMessageId = Truncate(providerMessageId, MaxProviderMessageIdLength);
        Content = sanitizedContent;
        Error = null;
        FailedAt = null;
        UpdatedAt = utcNow;
    }

    public void ApplyDeliveryStatus(NotificationStatus incomingStatus, DateTimeOffset eventTimestamp, string? error = null)
    {
        if (Status == NotificationStatus.Cancelled)
            return;

        if (incomingStatus == NotificationStatus.Failed)
        {
            if (Status is NotificationStatus.Read or NotificationStatus.Failed)
                return;

            Status = NotificationStatus.Failed;
            FailedAt ??= eventTimestamp;
            if (!string.IsNullOrWhiteSpace(error))
                Error = Truncate(error, MaxErrorLength);
            UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }

        var currentRank = GetProgressRank(Status);
        var incomingRank = GetProgressRank(incomingStatus);

        if (incomingRank < 0)
            return;

        switch (incomingStatus)
        {
            case NotificationStatus.Sent:
                SentAt ??= eventTimestamp;
                break;
            case NotificationStatus.Delivered:
                SentAt ??= eventTimestamp;
                DeliveredAt ??= eventTimestamp;
                break;
            case NotificationStatus.Read:
                SentAt ??= eventTimestamp;
                DeliveredAt ??= eventTimestamp;
                ReadAt ??= eventTimestamp;
                break;
        }

        if (incomingRank > currentRank)
        {
            Status = incomingStatus;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void FallbackToChannel(NotificationChannel fallbackChannel, string failureReason, DateTimeOffset utcNow)
    {
        if (Channel == fallbackChannel)
            throw new InvalidOperationException($"El canal ya es '{fallbackChannel}'.");

        if (Status is NotificationStatus.Cancelled or NotificationStatus.Delivered or NotificationStatus.Read)
            throw new InvalidOperationException($"No se puede aplicar fallback en estado '{Status}'.");

        OriginalChannel ??= Channel;
        Channel = fallbackChannel;
        Status = NotificationStatus.Pending;
        Error = Truncate(failureReason, MaxErrorLength);
        UpdatedAt = utcNow;
    }

    public void RecordAttemptFailure(string error, bool isTerminal, DateTimeOffset utcNow)
    {
        if (Status == NotificationStatus.Cancelled)
            throw new InvalidOperationException("No se puede registrar fallo en una notificación cancelada.");

        Attempts++;
        Error = string.IsNullOrWhiteSpace(error)
            ? "Error desconocido al entregar notificación."
            : Truncate(error, MaxErrorLength);

        if (isTerminal)
        {
            Status = NotificationStatus.Failed;
            FailedAt = utcNow;
        }

        UpdatedAt = utcNow;
    }

    public void Cancel(string reason, DateTimeOffset utcNow)
    {
        if (Status is not NotificationStatus.Pending and not NotificationStatus.Scheduled)
            throw new InvalidOperationException($"Solo se pueden cancelar notificaciones pendientes o programadas (actual: '{Status}').");

        Status = NotificationStatus.Cancelled;
        Error = string.IsNullOrWhiteSpace(reason) ? "Cancelada." : Truncate(reason, MaxErrorLength);
        UpdatedAt = utcNow;
    }

    public void RedactContent(DateTimeOffset utcNow)
    {
        if (ContentRedactedAt.HasValue)
            return;

        Content = RedactedPlaceholder;
        TemplateParamsJson = "{}";
        ContentRedactedAt = utcNow;
        UpdatedAt = utcNow;
    }

    private static int GetProgressRank(NotificationStatus status) => status switch
    {
        NotificationStatus.Pending => 0,
        NotificationStatus.Scheduled => 0,
        NotificationStatus.Sent => 1,
        NotificationStatus.Delivered => 2,
        NotificationStatus.Read => 3,
        _ => -1
    };

    private static string Truncate(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
