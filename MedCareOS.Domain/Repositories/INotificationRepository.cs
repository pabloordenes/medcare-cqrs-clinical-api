using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Repositories;

public interface INotificationRepository
{
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Notification?> GetByProviderMessageIdAsync(string providerMessageId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByIdempotencyKeyAsync(Guid appointmentId, NotificationType type, DateTimeOffset scheduledFor, CancellationToken cancellationToken = default);
    Task<Notification?> GetLatestActionableByPatientAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<List<Notification>> GetPendingRedactionBatchAsync(DateTimeOffset createdBeforeUtc, int batchSize, CancellationToken cancellationToken = default);
    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);
}
