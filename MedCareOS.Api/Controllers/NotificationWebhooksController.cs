using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Notifications.Commands.ProcessProviderDeliveryStatus;
using MedCareOS.Application.Notifications.Commands.ProcessWhatsAppInboundMessage;
using MedCareOS.Domain.Enums;
using MedCareOS.Infrastructure.Configuration;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/webhooks/notifications")]
[AllowAnonymous]
[EnableRateLimiting("ProviderWebhooks")]
public class NotificationWebhooksController : ControllerBase
{
    private const int MaxWebhookBodyBytes = 262_144;

    private readonly IEnumerable<IWebhookSignatureVerifier> _verifiers;
    private readonly MetaWhatsAppOptions _whatsAppOptions;
    private readonly ISender _sender;
    private readonly ILogger<NotificationWebhooksController> _logger;

    public NotificationWebhooksController(
        IEnumerable<IWebhookSignatureVerifier> verifiers,
        IOptions<MetaWhatsAppOptions> whatsAppOptions,
        ISender sender,
        ILogger<NotificationWebhooksController> logger)
    {
        _verifiers = verifiers;
        _whatsAppOptions = whatsAppOptions.Value;
        _sender = sender;
        _logger = logger;
    }

    [HttpGet("whatsapp")]
    public IActionResult VerifyWhatsAppWebhook(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (!string.Equals(mode, "subscribe", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(_whatsAppOptions.WebhookVerifyToken) ||
            string.IsNullOrWhiteSpace(verifyToken))
        {
            return Forbid();
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(_whatsAppOptions.WebhookVerifyToken.Trim()));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(verifyToken.Trim()));

        if (!CryptographicOperations.FixedTimeEquals(expectedHash, providedHash))
            return Forbid();

        return Content(challenge ?? string.Empty, "text/plain", Encoding.UTF8);
    }

    [HttpPost("whatsapp")]
    [RequestSizeLimit(MaxWebhookBodyBytes)]
    public async Task<IActionResult> ReceiveWhatsAppWebhook(CancellationToken cancellationToken)
    {
        var rawBytes = await ReadRawBodyAsync(cancellationToken);
        if (!VerifySignature("MetaCloud", rawBytes))
            return Unauthorized(new { message = "Firma X-Hub-Signature-256 inválida." });

        using var doc = TryParseJson(rawBytes);
        if (doc is null)
            return BadRequest(new { message = "Payload JSON inválido." });

        if (!doc.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return Ok(new { processed = 0 });

        int processed = 0;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object)
                    continue;

                if (value.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var st in statuses.EnumerateArray())
                    {
                        var wamid = GetStringProperty(st, "id");
                        var statusRaw = GetStringProperty(st, "status");
                        if (string.IsNullOrWhiteSpace(wamid) || string.IsNullOrWhiteSpace(statusRaw))
                            continue;

                        var mappedStatus = MapMetaStatus(statusRaw);
                        if (!mappedStatus.HasValue)
                            continue;

                        var ts = ParseUnixTimestampOrUtcNow(st, "timestamp");
                        var errorDetail = ExtractMetaStatusError(st);
                        var eventId = $"{wamid}:{statusRaw}:{ts.ToUnixTimeSeconds()}";

                        await _sender.Send(
                            new ProcessProviderDeliveryStatusCommand(
                                Provider: "WhatsApp",
                                EventId: eventId,
                                ProviderMessageId: wamid,
                                Status: mappedStatus.Value,
                                EventTimestampUtc: ts,
                                Error: errorDetail),
                            cancellationToken);
                        processed++;
                    }
                }

                if (value.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                {
                    foreach (var msg in messages.EnumerateArray())
                    {
                        var msgId = GetStringProperty(msg, "id");
                        var fromPhone = GetStringProperty(msg, "from");
                        if (string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(fromPhone))
                            continue;

                        var ts = ParseUnixTimestampOrUtcNow(msg, "timestamp");
                        var (payload, textBody) = ExtractMetaInboundContent(msg);

                        await _sender.Send(
                            new ProcessWhatsAppInboundMessageCommand(
                                InboundMessageId: msgId,
                                FromPhone: fromPhone,
                                ButtonPayload: payload,
                                TextBody: textBody,
                                ReceivedAtUtc: ts),
                            cancellationToken);
                        processed++;
                    }
                }
            }
        }

        return Ok(new { processed });
    }

    [HttpPost("email")]
    [RequestSizeLimit(MaxWebhookBodyBytes)]
    public async Task<IActionResult> ReceiveEmailWebhook(CancellationToken cancellationToken)
    {
        var rawBytes = await ReadRawBodyAsync(cancellationToken);
        if (!VerifySignature("Resend", rawBytes))
            return Unauthorized(new { message = "Firma webhook Svix inválida." });

        using var doc = TryParseJson(rawBytes);
        if (doc is null)
            return BadRequest(new { message = "Payload JSON inválido." });

        var eventType = GetStringProperty(doc.RootElement, "type");
        if (string.IsNullOrWhiteSpace(eventType))
            return Ok(new { ignored = true });

        var (mappedStatus, triggerOptOut) = MapResendEvent(eventType);
        if (!mappedStatus.HasValue)
            return Ok(new { ignored = true, type = eventType });

        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return Ok(new { ignored = true });

        var emailId = GetStringProperty(data, "email_id");
        if (string.IsNullOrWhiteSpace(emailId))
            return Ok(new { ignored = true });

        var createdAt = doc.RootElement.TryGetProperty("created_at", out var createdProp) &&
                        createdProp.ValueKind == JsonValueKind.String &&
                        DateTimeOffset.TryParse(createdProp.GetString(), out var parsedTs)
            ? parsedTs
            : DateTimeOffset.UtcNow;

        var svixId = Request.Headers["svix-id"].ToString().Trim();
        var eventId = !string.IsNullOrWhiteSpace(svixId)
            ? svixId
            : $"{emailId}:{eventType}:{createdAt.ToUnixTimeMilliseconds()}";

        string? error = null;
        if (mappedStatus == NotificationStatus.Failed)
        {
            error = data.TryGetProperty("bounce", out var bounce) && bounce.ValueKind == JsonValueKind.Object
                ? GetStringProperty(bounce, "message") ?? eventType
                : eventType;
        }

        await _sender.Send(
            new ProcessProviderDeliveryStatusCommand(
                Provider: "Email",
                EventId: eventId,
                ProviderMessageId: emailId,
                Status: mappedStatus.Value,
                EventTimestampUtc: createdAt,
                Error: error,
                TriggerPatientOptOut: triggerOptOut),
            cancellationToken);

        return Ok(new { processed = 1 });
    }

    private async Task<byte[]> ReadRawBodyAsync(CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }

    private bool VerifySignature(string provider, byte[] rawBody)
    {
        var verifier = _verifiers.FirstOrDefault(v => string.Equals(v.Provider, provider, StringComparison.OrdinalIgnoreCase));
        if (verifier is null)
        {
            _logger.LogError("No se encontró verificador de firma webhook para proveedor '{Provider}'.", provider);
            return false;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in Request.Headers)
        {
            headers[h.Key] = h.Value.ToString();
        }

        return verifier.Verify(new WebhookSignatureContext(rawBody, headers, DateTimeOffset.UtcNow));
    }

    private static JsonDocument? TryParseJson(byte[] rawBytes)
    {
        if (rawBytes.Length == 0)
            return null;

        try
        {
            return JsonDocument.Parse(rawBytes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static NotificationStatus? MapMetaStatus(string statusRaw) => statusRaw.Trim().ToLowerInvariant() switch
    {
        "sent" => NotificationStatus.Sent,
        "delivered" => NotificationStatus.Delivered,
        "read" => NotificationStatus.Read,
        "failed" => NotificationStatus.Failed,
        _ => null
    };

    private static (NotificationStatus? Status, bool TriggerOptOut) MapResendEvent(string eventType) =>
        eventType.Trim().ToLowerInvariant() switch
        {
            "email.sent" => (NotificationStatus.Sent, false),
            "email.delivered" => (NotificationStatus.Delivered, false),
            "email.bounced" or "email.failed" => (NotificationStatus.Failed, false),
            "email.complained" => (NotificationStatus.Failed, true),
            _ => (null, false)
        };

    private static (string? Payload, string? TextBody) ExtractMetaInboundContent(JsonElement msg)
    {
        var msgType = GetStringProperty(msg, "type")?.ToLowerInvariant();
        switch (msgType)
        {
            case "button" when msg.TryGetProperty("button", out var btn):
                return (GetStringProperty(btn, "payload"), GetStringProperty(btn, "text"));

            case "interactive" when msg.TryGetProperty("interactive", out var inter) &&
                                    inter.TryGetProperty("button_reply", out var reply):
                return (GetStringProperty(reply, "id"), GetStringProperty(reply, "title"));

            case "text" when msg.TryGetProperty("text", out var txt):
                return (null, GetStringProperty(txt, "body"));

            default:
                return (null, null);
        }
    }

    private static string? ExtractMetaStatusError(JsonElement statusObj)
    {
        if (!statusObj.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var err in errors.EnumerateArray())
        {
            var title = GetStringProperty(err, "title");
            var message = GetStringProperty(err, "message");
            var code = err.TryGetProperty("code", out var c) ? c.ToString() : null;
            var combined = $"{code} {title ?? message}".Trim();
            if (!string.IsNullOrWhiteSpace(combined))
                return combined;
        }

        return null;
    }

    private static DateTimeOffset ParseUnixTimestampOrUtcNow(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), out var strSeconds))
                return DateTimeOffset.FromUnixTimeSeconds(strSeconds);
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var numSeconds))
                return DateTimeOffset.FromUnixTimeSeconds(numSeconds);
        }

        return DateTimeOffset.UtcNow;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }
}
