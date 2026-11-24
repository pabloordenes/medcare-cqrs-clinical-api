using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IRiskSignalRepository
{
    Task<bool> ExistsByIdempotencyKeyAsync(
        Guid appointmentId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<List<RiskSignal>> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    Task AddAsync(RiskSignal signal, CancellationToken cancellationToken = default);
}
