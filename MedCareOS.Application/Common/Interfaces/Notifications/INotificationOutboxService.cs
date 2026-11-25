using MedCareOS.Application.Notifications.Events;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Common.Interfaces.Notifications;

public interface INotificationOutboxService
{
    Task<NotificationEnqueueResult> EnqueueAsync(EnqueueNotificationRequest request, CancellationToken cancellationToken = default);
}

public enum NotificationEnqueueOutcome
{
    Enqueued,
    ScheduledWithoutImmediateDispatch,
    SkippedOptOut,
    SkippedNoChannel,
    DuplicateSkipped
}

public sealed record NotificationEnqueueResult(
    NotificationEnqueueOutcome Outcome,
    Guid? NotificationId = null,
    NotificationChannel? Channel = null,
    SendNotificationRequested? PendingDispatchEvent = null);

public sealed record EnqueueNotificationRequest(
    Guid TenantId,
    Guid? AppointmentId,
    Patient Patient,
    NotificationType Type,
    string TemplateKey,
    string Locale,
    DateTimeOffset ScheduledFor,
    IReadOnlyDictionary<string, string> Parameters,
    Guid CorrelationId,
    NotificationChannel? ChannelOverride = null,
    bool ForceImmediateDispatch = false,
    Guid? WaitingListOfferId = null,
    bool DeferPublishToCaller = false);
