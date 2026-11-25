using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class AiResolutionTaskRepository : IAiResolutionTaskRepository
{
    private readonly ApplicationDbContext _dbContext;

    public AiResolutionTaskRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(AiResolutionTask aiResolutionTask)
    {
        _dbContext.AiResolutionTasks.Add(aiResolutionTask);
    }

    public async Task<List<AiResolutionTask>> GetPendingTasksAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.AiResolutionTasks
            .Where(t => t.Status == AiResolutionStatus.Pending)
            .OrderByDescending(t => t.RiskProbability)
            .ToListAsync(cancellationToken);
    }

    public async Task<AiResolutionTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.AiResolutionTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<AiResolutionTask?> GetPendingByResourceIdAsync(Guid resourceId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AiResolutionTasks
            .FirstOrDefaultAsync(t => t.ResourceId == resourceId && t.Status == AiResolutionStatus.Pending, cancellationToken);
    }

    public async Task<List<AiResolutionTask>> GetAllPendingByResourceIdAsync(Guid resourceId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AiResolutionTasks
            .Where(t => t.ResourceId == resourceId && t.Status == AiResolutionStatus.Pending)
            .ToListAsync(cancellationToken);
    }
}
