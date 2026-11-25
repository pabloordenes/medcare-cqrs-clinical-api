using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IWaitingListOfferRepository
{
    Task<WaitingListOffer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WaitingListOffer?> GetPendingByScheduleBlockIdAsync(Guid scheduleBlockId, CancellationToken cancellationToken = default);
    Task<List<WaitingListOffer>> GetPendingByPatientIdAsync(Guid patientId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task<bool> HasActivePendingOfferForBlockAsync(Guid scheduleBlockId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task<HashSet<Guid>> GetActivePendingBlockIdsAsync(IReadOnlyCollection<Guid> scheduleBlockIds, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task<List<Guid>> GetAttemptedWaitingListIdsForBlockAsync(Guid scheduleBlockId, CancellationToken cancellationToken = default);
    Task<int> CountOffersForBlockAsync(Guid scheduleBlockId, CancellationToken cancellationToken = default);
    Task<List<WaitingListOffer>> ClaimExpiredPendingOffersAsync(DateTimeOffset utcNow, int batchSize, CancellationToken cancellationToken = default);
    Task AddAsync(WaitingListOffer offer, CancellationToken cancellationToken = default);
}
