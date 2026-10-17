using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class ScheduleBlockRepository : IScheduleBlockRepository
{
    private readonly ApplicationDbContext _dbContext;
    
    public ScheduleBlockRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ScheduleBlock>> GetAssignedBlocksInRangeAsync(Guid staffId,
        Guid boxId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken)
    {
        return await _dbContext.ScheduleBlocks.Where(
            sb => (sb.StaffId == staffId || sb.BoxId == boxId) 
                  && sb.StartTime >= startTime && sb.StartTime <= endTime)
            .AsNoTracking().ToListAsync(cancellationToken);
    }

    public void AddRange(IEnumerable<ScheduleBlock> scheduleBlock)
    {
        _dbContext.ScheduleBlocks.AddRange(scheduleBlock);
    }
}