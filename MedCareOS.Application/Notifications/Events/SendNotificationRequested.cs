namespace MedCareOS.Application.Notifications.Events;

public record SendNotificationRequested(
    Guid NotificationId,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc);
