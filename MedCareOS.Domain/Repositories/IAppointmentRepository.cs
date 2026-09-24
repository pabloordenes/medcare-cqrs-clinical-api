using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IAppointmentRepository
{
    Task AddAppointmentAsync(Appointment appointment, CancellationToken cancellationToken = default);
}