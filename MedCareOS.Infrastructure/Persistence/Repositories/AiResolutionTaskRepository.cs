using MedCareOS.Domain.Entities;
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
            .Where(t => t.Status == "Pending")
            .OrderByDescending(t => t.RiskProbability)
            .ToListAsync(cancellationToken);
    }

    public async Task<AiResolutionTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.AiResolutionTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }
}