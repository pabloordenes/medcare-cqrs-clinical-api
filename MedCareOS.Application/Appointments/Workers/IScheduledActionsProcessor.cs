namespace MedCareOS.Application.Appointments.Workers;

public sealed record ScheduledActionsBatchResult(
    int Claimed,
    int Completed,
    int Skipped,
    int Retried,
    int Failed);

public interface IScheduledActionsProcessor
{
    Task<ScheduledActionsBatchResult> ProcessDueBatchAsync(
        string? workerId = null,
        DateTimeOffset? utcNowOverride = null,
        CancellationToken cancellationToken = default);
}
