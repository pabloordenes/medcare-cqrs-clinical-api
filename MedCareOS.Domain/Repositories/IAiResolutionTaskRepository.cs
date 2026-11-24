using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IAiResolutionTaskRepository
{
    void Add(AiResolutionTask aiResolutionTask);
    Task<List<AiResolutionTask>> GetPendingTasksAsync(CancellationToken cancellationToken = default);
    Task<AiResolutionTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<AiResolutionTask?> GetPendingByResourceIdAsync(Guid resourceId, CancellationToken cancellationToken = default);
    Task<List<AiResolutionTask>> GetAllPendingByResourceIdAsync(Guid resourceId, CancellationToken cancellationToken = default);
}