using System.Security.Cryptography;
using System.Text;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace MedCareOS.Infrastructure.Notifications.Webhooks;

public class MetaWebhookSignatureVerifier : IWebhookSignatureVerifier
{
    private const string SignatureHeader = "X-Hub-Signature-256";
    private const string Prefix = "sha256=";

    private readonly MetaWhatsAppOptions _options;

    public MetaWebhookSignatureVerifier(IOptions<MetaWhatsAppOptions> options)
    {
        _options = options.Value;
    }

    public string Provider => "MetaCloud";

    public bool Verify(WebhookSignatureContext context)
    {
        if (string.IsNullOrWhiteSpace(_options.AppSecret))
            return false;

        if (!context.Headers.TryGetValue(SignatureHeader, out var headerValue) ||
            string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        var trimmedHeader = headerValue.Trim();
        if (!trimmedHeader.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var hexSignature = trimmedHeader[Prefix.Length..];
        if (hexSignature.Length != 64)
            return false;

        byte[] providedBytes;
        try
        {
            providedBytes = Convert.FromHexString(hexSignature);
        }
        catch (FormatException)
        {
            return false;
        }

        var computedBytes = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_options.AppSecret.Trim()),
            context.RawBody.Span);

        return CryptographicOperations.FixedTimeEquals(providedBytes, computedBytes);
    }
}
