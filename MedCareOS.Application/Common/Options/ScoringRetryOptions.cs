namespace MedCareOS.Application.Common.Options;

public class ScoringRetryOptions
{
    public const string SectionName = "ScoringRetry";

    public int PollIntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 5;
    public int PendingStaleMinutes { get; set; } = 5;
    public int BaseBackoffMinutes { get; set; } = 2;
    public int MaxBackoffMinutes { get; set; } = 60;
    public int LeaseMinutes { get; set; } = 5;

    public TimeSpan ComputeBackoff(int attempts)
    {
        var exponent = Math.Max(0, attempts - 1);
        var baseMinutes = Math.Max(1, BaseBackoffMinutes);
        var maxMinutes = Math.Max(baseMinutes, MaxBackoffMinutes);
        var minutes = Math.Min(maxMinutes, baseMinutes * Math.Pow(2, exponent));
        return TimeSpan.FromMinutes(minutes);
    }
}
