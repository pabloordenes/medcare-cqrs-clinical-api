namespace MedCareOS.Application.Common.Interfaces;

public interface IMessageInboxStore
{
    Task<bool> HasBeenCompletedAsync(Guid messageId, string consumerName, CancellationToken cancellationToken = default);
    Task MarkCompletedAsync(Guid messageId, string consumerName, TimeSpan retention, CancellationToken cancellationToken = default);
}
