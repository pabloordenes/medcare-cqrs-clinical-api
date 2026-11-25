using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IAppointmentRescheduleHistoryRepository
{
    Task AddAsync(AppointmentRescheduleHistory history, CancellationToken cancellationToken = default);
    Task<List<AppointmentRescheduleHistory>> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken = default);
}
