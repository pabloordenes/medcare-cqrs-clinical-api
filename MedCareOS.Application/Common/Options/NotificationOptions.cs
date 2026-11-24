namespace MedCareOS.Application.Common.Options;

public class NotificationOptions
{
    public const string SectionName = "Notifications";

    public string ClinicName { get; set; } = "Clínica MedCare";
    public string TimeZoneId { get; set; } = "America/Santiago";
    public string PublicBaseUrl { get; set; } = "https://app.medcare.cl";
    public string ActionTokenSecret { get; set; } = string.Empty;
    public int ActionTokenDefaultTtlHours { get; set; } = 48;
    public int MaxPrimaryChannelAttempts { get; set; } = 3;
    public bool IncludeSpecialtyInMessages { get; set; } = true;
    public string[] SensitiveSpecialtiesExcluded { get; set; } = [];
    public int ContentRetentionDays { get; set; } = 30;
    public int RetentionPollIntervalMinutes { get; set; } = 60;
    public int RetentionBatchSize { get; set; } = 100;
    public string EmailProvider { get; set; } = "Fake";
    public string WhatsAppProvider { get; set; } = "Fake";
    public NotificationRateLimitOptions RateLimiting { get; set; } = new();
}

public class NotificationRateLimitOptions
{
    public int ProviderWebhooksPermitLimit { get; set; } = 120;
    public int ProviderWebhooksWindowSeconds { get; set; } = 60;
    public int PublicActionsPermitLimit { get; set; } = 15;
    public int PublicActionsWindowSeconds { get; set; } = 60;
}
