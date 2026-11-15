using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IAiResolutionTaskRepository
{
    void Add(AiResolutionTask aiResolutionTask);
    Task<List<AiResolutionTask>> GetPendingTasksAsync(CancellationToken cancellationToken = default);
    Task<AiResolutionTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}