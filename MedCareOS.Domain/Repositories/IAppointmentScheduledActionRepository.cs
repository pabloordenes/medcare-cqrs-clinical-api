using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IAppointmentScheduledActionRepository
{
    Task AddRangeAsync(IEnumerable<AppointmentScheduledAction> actions, CancellationToken cancellationToken = default);
    Task<AppointmentScheduledAction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<AppointmentScheduledAction>> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken = default);
    Task<List<AppointmentScheduledAction>> GetPendingByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken = default);
    Task<int> GetMaxPlanVersionAsync(Guid appointmentId, CancellationToken cancellationToken = default);
    Task<List<AppointmentScheduledAction>> ClaimDueBatchAsync(
        string workerId,
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken = default);
}
