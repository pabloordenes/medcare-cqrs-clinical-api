using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Common.Interfaces.Notifications;

public interface INotificationTemplateRenderer
{
    Task<RenderedNotification> RenderAsync(TemplateRenderRequest request, CancellationToken cancellationToken = default);
}

public sealed record TemplateRenderRequest(
    Guid TenantId,
    Guid? AppointmentId,
    NotificationType Type,
    NotificationChannel Channel,
    string TemplateKey,
    string Locale,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyDictionary<string, string>? ActionUrls = null,
    string? RescheduleTokenSuffix = null);
