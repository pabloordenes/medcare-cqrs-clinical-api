using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IPatientActionTokenRepository
{
    Task<PatientActionToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task RevokeActiveForNotificationAsync(Guid notificationId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task RevokeActiveForWaitingListOfferAsync(Guid waitingListOfferId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task AddAsync(PatientActionToken token, CancellationToken cancellationToken = default);
    Task<int> DeleteExpiredBeforeAsync(DateTimeOffset cutoffUtc, int batchSize, CancellationToken cancellationToken = default);
}
