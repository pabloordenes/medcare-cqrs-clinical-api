namespace MedCareOS.Infrastructure.Configuration;

public class AiServiceOptions
{
    public const string SectionName = "AiService";
    
    public string BaseUrl { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string PrivateKeyPath { get; set; } = string.Empty;
}