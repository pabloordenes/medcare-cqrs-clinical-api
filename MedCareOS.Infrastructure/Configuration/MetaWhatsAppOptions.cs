namespace MedCareOS.Infrastructure.Configuration;

public class MetaWhatsAppOptions
{
    public const string SectionName = "Notifications:WhatsApp";

    public string BaseUrl { get; set; } = "https://graph.facebook.com/";
    public string ApiVersion { get; set; } = "v21.0";
    public string PhoneNumberId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string WebhookVerifyToken { get; set; } = string.Empty;
    public string DefaultTemplateLanguage { get; set; } = "es";
    public int TimeoutSeconds { get; set; } = 15;
}
