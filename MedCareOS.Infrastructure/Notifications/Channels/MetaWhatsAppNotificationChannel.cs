using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Domain.Common;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedCareOS.Infrastructure.Notifications.Channels;

public class MetaWhatsAppNotificationChannel : INotificationChannel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<int> InvalidRecipientErrorCodes = [131009, 131026, 131030, 131051];
    private static readonly HashSet<int> PermanentTemplateOrWindowErrorCodes = [131047, 132000, 132001, 132005, 132007, 132012];
    private static readonly HashSet<int> RateLimitErrorCodes = [4, 80007, 131048, 131056];

    private readonly HttpClient _httpClient;
    private readonly MetaWhatsAppOptions _options;
    private readonly ILogger<MetaWhatsAppNotificationChannel> _logger;

    public MetaWhatsAppNotificationChannel(
        HttpClient httpClient,
        IOptions<MetaWhatsAppOptions> options,
        ILogger<MetaWhatsAppNotificationChannel> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public NotificationChannel Channel => NotificationChannel.WhatsApp;
    public string ProviderName => "MetaCloud";

    public async Task<DeliveryResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.PhoneNumberId) || string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            return DeliveryResult.Failure(
                ProviderName,
                DeliveryFailureKind.Permanent,
                "Configuración de Meta WhatsApp Cloud API incompleta (PhoneNumberId o AccessToken faltante).");
        }

        var normalizedTo = PhoneNormalizer.ToWhatsAppDigits(message.RecipientAddress);
        if (string.IsNullOrWhiteSpace(normalizedTo))
        {
            return DeliveryResult.Failure(
                ProviderName,
                DeliveryFailureKind.InvalidRecipient,
                "Número telefónico inválido para WhatsApp.");
        }

        if (string.IsNullOrWhiteSpace(message.Rendered.MetaTemplateName))
        {
            return DeliveryResult.Failure(
                ProviderName,
                DeliveryFailureKind.Permanent,
                $"La plantilla '{message.TemplateKey}' no tiene un MetaTemplateName configurado.");
        }

        var timeoutSeconds = _options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 15;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var components = BuildTemplateComponents(message.Rendered);
        var languageCode = string.IsNullOrWhiteSpace(_options.DefaultTemplateLanguage)
            ? "es"
            : _options.DefaultTemplateLanguage.Trim();

        var payload = new MetaTemplateMessageRequest(
            MessagingProduct: "whatsapp",
            RecipientType: "individual",
            To: normalizedTo,
            Type: "template",
            Template: new MetaTemplatePayload(
                Name: message.Rendered.MetaTemplateName.Trim(),
                Language: new MetaLanguagePayload(languageCode),
                Components: components));

        var apiVersion = string.IsNullOrWhiteSpace(_options.ApiVersion) ? "v21.0" : _options.ApiVersion.Trim('/');
        var relativePath = $"{apiVersion}/{_options.PhoneNumberId.Trim()}/messages";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken.Trim());

            using var response = await _httpClient.SendAsync(request, cts.Token);
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                var success = await response.Content.ReadFromJsonAsync<MetaSendMessageResponse>(cancellationToken: cts.Token);
                var messageId = success?.Messages?.FirstOrDefault()?.Id;
                if (string.IsNullOrWhiteSpace(messageId))
                {
                    return DeliveryResult.Failure(
                        ProviderName,
                        DeliveryFailureKind.Permanent,
                        "Meta Graph API devolvió 2xx sin wamid en messages[0].id.",
                        statusCode);
                }

                return DeliveryResult.Success(messageId.Trim(), ProviderName);
            }

            var rawError = await ReadTruncatedBodyAsync(response, cts.Token);
            var (errorCode, errorMessage) = TryParseMetaError(rawError);
            var failureKind = ClassifyError(response.StatusCode, errorCode);

            _logger.LogWarning(
                "Meta WhatsApp rechazó notificación {NotificationId}. Status={StatusCode}, MetaCode={MetaCode}, Kind={FailureKind}, Detail={Detail}",
                message.NotificationId,
                statusCode,
                errorCode,
                failureKind,
                errorMessage ?? rawError);

            return DeliveryResult.Failure(
                ProviderName,
                failureKind,
                errorMessage ?? $"Meta HTTP {statusCode}: {rawError}",
                statusCode,
                errorCode?.ToString());
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Timeout ({TimeoutSeconds}s) enviando WhatsApp vía Meta Cloud API para {NotificationId}.", timeoutSeconds, message.NotificationId);
            return DeliveryResult.Failure(ProviderName, DeliveryFailureKind.Transient, $"Timeout ({timeoutSeconds}s) invocando Meta Graph API.", 408);
        }
        catch (HttpRequestException ex)
        {
            var statusCode = ex.StatusCode.HasValue ? (int?)ex.StatusCode.Value : null;
            var failureKind = ex.StatusCode.HasValue
                ? ClassifyError(ex.StatusCode.Value, null)
                : DeliveryFailureKind.Transient;

            _logger.LogWarning(ex, "Error de red/HTTP invocando Meta Cloud API para {NotificationId}.", message.NotificationId);
            return DeliveryResult.Failure(ProviderName, failureKind, ex.Message, statusCode);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error parseando JSON de Meta Cloud API para {NotificationId}.", message.NotificationId);
            return DeliveryResult.Failure(ProviderName, DeliveryFailureKind.Permanent, $"Respuesta JSON inválida de Meta: {ex.Message}");
        }
    }

    private static List<MetaComponentPayload> BuildTemplateComponents(RenderedNotification rendered)
    {
        var components = new List<MetaComponentPayload>();

        if (rendered.MetaOrderedBodyParameters.Count > 0)
        {
            var bodyParams = rendered.MetaOrderedBodyParameters
                .Select(val => new MetaComponentParameter("text", Text: val, Payload: null))
                .ToList();

            components.Add(new MetaComponentPayload(
                Type: "body",
                SubType: null,
                Index: null,
                Parameters: bodyParams));
        }

        for (int i = 0; i < rendered.Buttons.Count; i++)
        {
            var button = rendered.Buttons[i];
            if (!string.IsNullOrWhiteSpace(button.Url))
            {
                components.Add(new MetaComponentPayload(
                    Type: "button",
                    SubType: "url",
                    Index: i.ToString(),
                    Parameters: [new MetaComponentParameter("text", Text: button.Url, Payload: null)]));
            }
            else
            {
                components.Add(new MetaComponentPayload(
                    Type: "button",
                    SubType: "quick_reply",
                    Index: i.ToString(),
                    Parameters: [new MetaComponentParameter("payload", Text: null, Payload: button.Id)]));
            }
        }

        return components;
    }

    private static DeliveryFailureKind ClassifyError(HttpStatusCode statusCode, int? metaErrorCode)
    {
        if (metaErrorCode.HasValue)
        {
            if (InvalidRecipientErrorCodes.Contains(metaErrorCode.Value))
                return DeliveryFailureKind.InvalidRecipient;
            if (PermanentTemplateOrWindowErrorCodes.Contains(metaErrorCode.Value))
                return DeliveryFailureKind.Permanent;
            if (RateLimitErrorCodes.Contains(metaErrorCode.Value))
                return DeliveryFailureKind.RateLimited;
        }

        return statusCode switch
        {
            HttpStatusCode.TooManyRequests => DeliveryFailureKind.RateLimited,
            HttpStatusCode.RequestTimeout => DeliveryFailureKind.Transient,
            >= HttpStatusCode.InternalServerError => DeliveryFailureKind.Transient,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => DeliveryFailureKind.Permanent,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => DeliveryFailureKind.Permanent,
            _ => DeliveryFailureKind.Permanent
        };
    }

    private static (int? Code, string? Message) TryParseMetaError(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            if (!doc.RootElement.TryGetProperty("error", out var err))
                return (null, null);

            int? code = err.TryGetProperty("code", out var codeProp) && codeProp.TryGetInt32(out var parsedCode)
                ? parsedCode
                : null;
            string? message = err.TryGetProperty("message", out var msgProp)
                ? msgProp.GetString()
                : null;

            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static async Task<string> ReadTruncatedBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            return raw.Length <= 500 ? raw : raw[..500];
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed record MetaTemplateMessageRequest(
        [property: JsonPropertyName("messaging_product")] string MessagingProduct,
        [property: JsonPropertyName("recipient_type")] string RecipientType,
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("template")] MetaTemplatePayload Template);

    private sealed record MetaTemplatePayload(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("language")] MetaLanguagePayload Language,
        [property: JsonPropertyName("components")] List<MetaComponentPayload> Components);

    private sealed record MetaLanguagePayload(
        [property: JsonPropertyName("code")] string Code);

    private sealed record MetaComponentPayload(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("sub_type")] string? SubType,
        [property: JsonPropertyName("index")] string? Index,
        [property: JsonPropertyName("parameters")] List<MetaComponentParameter> Parameters);

    private sealed record MetaComponentParameter(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("payload")] string? Payload);

    private sealed record MetaSendMessageResponse(
        [property: JsonPropertyName("messages")] List<MetaSentMessageItem>? Messages);

    private sealed record MetaSentMessageItem(
        [property: JsonPropertyName("id")] string? Id);
}
