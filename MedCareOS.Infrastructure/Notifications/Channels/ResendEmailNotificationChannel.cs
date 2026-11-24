using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedCareOS.Infrastructure.Notifications.Channels;

public class ResendEmailNotificationChannel : INotificationChannel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ResendOptions _options;
    private readonly ILogger<ResendEmailNotificationChannel> _logger;

    public ResendEmailNotificationChannel(
        HttpClient httpClient,
        IOptions<ResendOptions> options,
        ILogger<ResendEmailNotificationChannel> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public NotificationChannel Channel => NotificationChannel.Email;
    public string ProviderName => "Resend";

    public async Task<DeliveryResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            return DeliveryResult.Failure(
                ProviderName,
                DeliveryFailureKind.Permanent,
                "Configuración de Resend incompleta (ApiKey o FromAddress faltante).");
        }

        if (string.IsNullOrWhiteSpace(message.RecipientAddress))
        {
            return DeliveryResult.Failure(
                ProviderName,
                DeliveryFailureKind.InvalidRecipient,
                "Dirección de email de destino vacía.");
        }

        var timeoutSeconds = _options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 15;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var fromHeader = string.IsNullOrWhiteSpace(_options.FromName)
            ? _options.FromAddress.Trim()
            : $"{_options.FromName.Trim()} <{_options.FromAddress.Trim()}>";

        var payload = new ResendSendEmailRequest(
            From: fromHeader,
            To: [message.RecipientAddress.Trim()],
            Subject: message.Rendered.Subject ?? "Notificación de cita médica",
            Text: message.Rendered.PlainTextBody,
            Html: message.Rendered.HtmlBody,
            ReplyTo: string.IsNullOrWhiteSpace(_options.ReplyToAddress) ? null : _options.ReplyToAddress.Trim(),
            Headers: new Dictionary<string, string>
            {
                ["X-Entity-Ref-ID"] = message.NotificationId.ToString("N"),
                ["X-Correlation-ID"] = message.CorrelationId.ToString("N")
            },
            Tags:
            [
                new ResendEmailTag("notification_id", message.NotificationId.ToString("N")),
                new ResendEmailTag("type", message.Type.ToString())
            ]);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey.Trim());
            request.Headers.TryAddWithoutValidation("Idempotency-Key", message.NotificationId.ToString("N"));

            using var response = await _httpClient.SendAsync(request, cts.Token);
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                var successBody = await response.Content.ReadFromJsonAsync<ResendSendEmailResponse>(cancellationToken: cts.Token);
                if (successBody is null || string.IsNullOrWhiteSpace(successBody.Id))
                {
                    return DeliveryResult.Failure(
                        ProviderName,
                        DeliveryFailureKind.Permanent,
                        "Resend devolvió 2xx sin identificador de mensaje.",
                        statusCode);
                }

                return DeliveryResult.Success(successBody.Id.Trim(), ProviderName);
            }

            var errorBody = await ReadTruncatedBodyAsync(response, cts.Token);
            var failureKind = ClassifyHttpStatus(response.StatusCode);

            _logger.LogWarning(
                "Resend rechazó envío de notificación {NotificationId}. Status={StatusCode}, Kind={FailureKind}, Body={Body}",
                message.NotificationId,
                statusCode,
                failureKind,
                errorBody);

            return DeliveryResult.Failure(
                ProviderName,
                failureKind,
                $"Resend HTTP {statusCode}: {errorBody}",
                statusCode);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Timeout ({TimeoutSeconds}s) enviando email vía Resend para {NotificationId}.", timeoutSeconds, message.NotificationId);
            return DeliveryResult.Failure(ProviderName, DeliveryFailureKind.Transient, $"Timeout ({timeoutSeconds}s) invocando Resend.", 408);
        }
        catch (HttpRequestException ex)
        {
            var statusCode = ex.StatusCode.HasValue ? (int?)ex.StatusCode.Value : null;
            var failureKind = ex.StatusCode.HasValue
                ? ClassifyHttpStatus(ex.StatusCode.Value)
                : DeliveryFailureKind.Transient;

            _logger.LogWarning(ex, "Error de red/HTTP invocando Resend para {NotificationId}.", message.NotificationId);
            return DeliveryResult.Failure(ProviderName, failureKind, ex.Message, statusCode);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parseando respuesta JSON de Resend para {NotificationId}.", message.NotificationId);
            return DeliveryResult.Failure(ProviderName, DeliveryFailureKind.Permanent, $"Respuesta JSON inválida de Resend: {ex.Message}");
        }
    }

    private static DeliveryFailureKind ClassifyHttpStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => DeliveryFailureKind.RateLimited,
        HttpStatusCode.RequestTimeout => DeliveryFailureKind.Transient,
        >= HttpStatusCode.InternalServerError => DeliveryFailureKind.Transient,
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => DeliveryFailureKind.InvalidRecipient,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => DeliveryFailureKind.Permanent,
        _ => DeliveryFailureKind.Permanent
    };

    private static async Task<string> ReadTruncatedBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            return raw.Length <= 300 ? raw : raw[..300];
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed record ResendSendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] IReadOnlyList<string> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("html")] string? Html,
        [property: JsonPropertyName("reply_to")] string? ReplyTo,
        [property: JsonPropertyName("headers")] Dictionary<string, string> Headers,
        [property: JsonPropertyName("tags")] IReadOnlyList<ResendEmailTag> Tags);

    private sealed record ResendEmailTag(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("value")] string Value);

    private sealed record ResendSendEmailResponse(
        [property: JsonPropertyName("id")] string? Id);
}
