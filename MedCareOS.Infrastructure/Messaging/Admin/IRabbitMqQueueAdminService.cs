namespace MedCareOS.Infrastructure.Messaging.Admin;

public record ErrorQueueSummaryDto(
    string QueueName,
    string ErrorQueueName,
    uint MessageCount,
    bool Exists);

public record RequeueResultDto(
    string QueueName,
    string ErrorQueueName,
    int RequeuedCount,
    int FailedCount,
    uint RemainingApprox);

public interface IRabbitMqQueueAdminService
{
    IReadOnlyCollection<string> AllowedQueues { get; }
    Task<List<ErrorQueueSummaryDto>> GetErrorQueueSummariesAsync(CancellationToken cancellationToken = default);
    Task<RequeueResultDto> RequeueErrorMessagesAsync(string queueName, int maxMessages, CancellationToken cancellationToken = default);
}
