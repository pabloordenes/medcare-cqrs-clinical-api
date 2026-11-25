namespace MedCareOS.Application.Common.Interfaces.Notifications;

public interface IWebhookSignatureVerifier
{
    string Provider { get; }
    bool Verify(WebhookSignatureContext context);
}

public sealed record WebhookSignatureContext(
    ReadOnlyMemory<byte> RawBody,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset UtcNow);
