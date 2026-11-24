namespace MedCareOS.Infrastructure.Configuration;

public class ResendOptions
{
    public const string SectionName = "Notifications:Resend";

    public string BaseUrl { get; set; } = "https://api.resend.com/";
    public string ApiKey { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "notificaciones@medcare.cl";
    public string FromName { get; set; } = "Clínica MedCare";
    public string? ReplyToAddress { get; set; }
    public int TimeoutSeconds { get; set; } = 15;
    public string WebhookSecret { get; set; } = string.Empty;
}
