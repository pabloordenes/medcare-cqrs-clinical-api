using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
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
                sb => !sb.IsOverbook
                      && (sb.StaffId == staffId || sb.BoxId == boxId)
                      && sb.StartTime >= startTime && sb.StartTime <= endTime)
            .AsNoTracking().ToListAsync(cancellationToken);
    }

    public void AddRange(IEnumerable<ScheduleBlock> scheduleBlock)
    {
        _dbContext.ScheduleBlocks.AddRange(scheduleBlock);
    }

    public async Task<ScheduleBlock?> GetByIdAsync(Guid scheduleBlockId, CancellationToken cancellationToken)
    {
        return await _dbContext.ScheduleBlocks.FirstOrDefaultAsync(sb => sb.Id == scheduleBlockId, cancellationToken);
    }

    public async Task<ScheduleBlock?> GetOverbookByAppointmentIdAsync(Guid sourceAppointmentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ScheduleBlocks
            .AsNoTracking()
            .FirstOrDefaultAsync(sb => sb.IsOverbook && sb.SourceAppointmentId == sourceAppointmentId, cancellationToken);
    }

    public async Task<ScheduleBlock?> GetOverbookByAppointmentIdTrackedAsync(Guid sourceAppointmentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ScheduleBlocks
            .FirstOrDefaultAsync(sb => sb.IsOverbook && sb.SourceAppointmentId == sourceAppointmentId, cancellationToken);
    }

    public async Task<int> CountActiveOverbooksForBoxSlotAsync(
        Guid boxId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ScheduleBlocks
            .AsNoTracking()
            .CountAsync(
                sb => sb.IsOverbook
                      && sb.BoxId == boxId
                      && (sb.OverbookStatus == OverbookBlockStatus.Open
                          || sb.OverbookStatus == OverbookBlockStatus.Offered
                          || sb.OverbookStatus == OverbookBlockStatus.Occupied)
                      && sb.StartTime < endTime
                      && sb.EndTime > startTime,
                cancellationToken);
    }
}
