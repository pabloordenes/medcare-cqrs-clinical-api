namespace MedCareOS.Application.Common.Options;

public class ScheduledActionsOptions
{
    public const string SectionName = "ScheduledActions";

    public int PollIntervalSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 25;
    public int LeaseDurationSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 5;
    public int BaseRetryBackoffMinutes { get; set; } = 2;
    public int MaxRetryBackoffMinutes { get; set; } = 30;

    public TimeSpan ComputeBackoff(int attempt)
    {
        var baseMinutes = Math.Max(1, BaseRetryBackoffMinutes);
        var maxMinutes = Math.Max(baseMinutes, MaxRetryBackoffMinutes);
        var exponent = Math.Clamp(attempt - 1, 0, 10);
        var computedMinutes = Math.Min(maxMinutes, baseMinutes * Math.Pow(2, exponent));
        return TimeSpan.FromMinutes(computedMinutes);
    }
}
