using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class ReceptionTaskRepository : IReceptionTaskRepository
{
    private readonly ApplicationDbContext _dbContext;

    public ReceptionTaskRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ReceptionTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ReceptionTasks
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<bool> HasActiveTaskAsync(
        Guid appointmentId,
        ReceptionTaskType taskType,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ReceptionTasks
            .AsNoTracking()
            .AnyAsync(
                t => t.AppointmentId == appointmentId
                     && t.TaskType == taskType
                     && (t.Status == ReceptionTaskStatus.Open || t.Status == ReceptionTaskStatus.InProgress),
                cancellationToken);
    }

    public async Task<ReceptionTask?> GetActiveTaskAsync(
        Guid appointmentId,
        ReceptionTaskType taskType,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ReceptionTasks
            .FirstOrDefaultAsync(
                t => t.AppointmentId == appointmentId
                     && t.TaskType == taskType
                     && (t.Status == ReceptionTaskStatus.Open || t.Status == ReceptionTaskStatus.InProgress),
                cancellationToken);
    }

    public async Task AddAsync(ReceptionTask task, CancellationToken cancellationToken = default)
    {
        await _dbContext.ReceptionTasks.AddAsync(task, cancellationToken);
    }
}
