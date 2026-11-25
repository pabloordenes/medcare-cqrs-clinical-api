namespace MedCareOS.Application.WaitingLists.Services;

public interface IWaitingListOfferCancellationService
{
    Task<int> CancelPendingOffersForOverbookUndoAsync(
        Guid overbookBlockId,
        string reason,
        DateTimeOffset utcNow,
        Guid correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class NoOpWaitingListOfferCancellationService : IWaitingListOfferCancellationService
{
    public Task<int> CancelPendingOffersForOverbookUndoAsync(
        Guid overbookBlockId,
        string reason,
        DateTimeOffset utcNow,
        Guid correlationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}
