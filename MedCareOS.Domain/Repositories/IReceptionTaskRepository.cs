using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Repositories;

public interface IReceptionTaskRepository
{
    Task<ReceptionTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> HasActiveTaskAsync(Guid appointmentId, ReceptionTaskType taskType, CancellationToken cancellationToken = default);
    Task<ReceptionTask?> GetActiveTaskAsync(Guid appointmentId, ReceptionTaskType taskType, CancellationToken cancellationToken = default);
    Task AddAsync(ReceptionTask task, CancellationToken cancellationToken = default);
}
