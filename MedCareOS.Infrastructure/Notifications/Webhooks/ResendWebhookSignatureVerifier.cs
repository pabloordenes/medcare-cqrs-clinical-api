using System.Security.Cryptography;
using System.Text;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace MedCareOS.Infrastructure.Notifications.Webhooks;

public class ResendWebhookSignatureVerifier : IWebhookSignatureVerifier
{
    private const int MaxTimestampToleranceSeconds = 300;
    private const string WhsecPrefix = "whsec_";

    private readonly ResendOptions _options;

    public ResendWebhookSignatureVerifier(IOptions<ResendOptions> options)
    {
        _options = options.Value;
    }

    public string Provider => "Resend";

    public bool Verify(WebhookSignatureContext context)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
            return false;

        if (!context.Headers.TryGetValue("svix-id", out var svixId) || string.IsNullOrWhiteSpace(svixId) ||
            !context.Headers.TryGetValue("svix-timestamp", out var svixTimestamp) || string.IsNullOrWhiteSpace(svixTimestamp) ||
            !context.Headers.TryGetValue("svix-signature", out var svixSignature) || string.IsNullOrWhiteSpace(svixSignature))
        {
            return false;
        }

        if (!long.TryParse(svixTimestamp.Trim(), out var unixSeconds))
            return false;

        var timestampUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (Math.Abs((context.UtcNow - timestampUtc).TotalSeconds) > MaxTimestampToleranceSeconds)
            return false;

        byte[] keyBytes;
        try
        {
            var rawSecret = _options.WebhookSecret.Trim();
            var base64Secret = rawSecret.StartsWith(WhsecPrefix, StringComparison.Ordinal)
                ? rawSecret[WhsecPrefix.Length..]
                : rawSecret;
            keyBytes = Convert.FromBase64String(base64Secret);
        }
        catch (FormatException)
        {
            return false;
        }

        if (keyBytes.Length == 0)
            return false;

        var rawBodyText = Encoding.UTF8.GetString(context.RawBody.Span);
        var signedPayload = $"{svixId.Trim()}.{svixTimestamp.Trim()}.{rawBodyText}";

        using var hmac = new HMACSHA256(keyBytes);
        var expectedSignature = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));

        var signatures = svixSignature.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in signatures)
        {
            var parts = entry.Split(',', 2);
            if (parts.Length != 2 || !string.Equals(parts[0], "v1", StringComparison.Ordinal))
                continue;

            try
            {
                var provided = Convert.FromBase64String(parts[1]);
                if (provided.Length == expectedSignature.Length &&
                    CryptographicOperations.FixedTimeEquals(provided, expectedSignature))
                {
                    return true;
                }
            }
            catch (FormatException)
            {
                // Ignore malformed individual signature entry and keep checking remaining entries
            }
        }

        return false;
    }
}
