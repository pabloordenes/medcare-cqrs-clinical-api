using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Common.Interfaces.Notifications;

public interface INotificationChannel
{
    NotificationChannel Channel { get; }
    string ProviderName { get; }
    Task<DeliveryResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}

public enum DeliveryFailureKind
{
    None,
    Transient,
    Permanent,
    InvalidRecipient,
    OptOutOrBlocked,
    RateLimited
}

public sealed record DeliveryResult(
    bool IsSuccess,
    string? ProviderMessageId,
    string ProviderName,
    DeliveryFailureKind FailureKind = DeliveryFailureKind.None,
    int? HttpStatusCode = null,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool ShouldFallbackImmediately =>
        !IsSuccess && FailureKind is DeliveryFailureKind.Permanent
            or DeliveryFailureKind.InvalidRecipient
            or DeliveryFailureKind.OptOutOrBlocked;

    public static DeliveryResult Success(string providerMessageId, string providerName) =>
        new(true, providerMessageId, providerName);

    public static DeliveryResult Failure(
        string providerName,
        DeliveryFailureKind failureKind,
        string errorMessage,
        int? httpStatusCode = null,
        string? errorCode = null) =>
        new(false, null, providerName, failureKind, httpStatusCode, errorCode, errorMessage);
}

public sealed record NotificationMessage(
    Guid NotificationId,
    Guid TenantId,
    Guid? AppointmentId,
    Guid PatientId,
    NotificationType Type,
    NotificationChannel Channel,
    string RecipientAddress,
    string TemplateKey,
    int TemplateVersion,
    string Locale,
    IReadOnlyDictionary<string, string> Parameters,
    RenderedNotification Rendered,
    Guid CorrelationId);

public sealed record RenderedNotification(
    string? Subject,
    string PlainTextBody,
    string? HtmlBody,
    string SanitizedContentForStorage,
    string? MetaTemplateName,
    IReadOnlyList<string> MetaOrderedBodyParameters,
    IReadOnlyList<NotificationActionButton> Buttons);

public sealed record NotificationActionButton(string Id, string Label, string? Url = null);
