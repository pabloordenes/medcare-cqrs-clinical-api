namespace MedCareOS.Domain.Entities;

public class ProcessedWebhookKey
{
    public string Key { get; private set; } = string.Empty;
    public string Endpoint { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private ProcessedWebhookKey() { } // ef core

    public static ProcessedWebhookKey Create(string key, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("La clave de idempotencia no puede estar vacía.", nameof(key));

        return new ProcessedWebhookKey
        {
            Key = key.Trim(),
            Endpoint = endpoint,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
