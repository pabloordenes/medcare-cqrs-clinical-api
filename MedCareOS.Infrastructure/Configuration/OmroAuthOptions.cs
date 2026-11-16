namespace MedCareOS.Infrastructure.Configuration;

public class OmroAuthOptions
{
    public const string SectionName = "OmroAuth";

    public string Issuer { get; set; } = "omro-orchestrator";
    public string Audience { get; set; } = "medcareos-api";
    public string Role { get; set; } = "omro_service";
    public string PublicKeyPath { get; set; } = string.Empty;
    public string PublicKeyContent { get; set; } = string.Empty;
}
