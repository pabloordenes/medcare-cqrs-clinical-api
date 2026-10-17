using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IScheduleBlockRepository
{
    Task<List<ScheduleBlock>> GetAssignedBlocksInRangeAsync(Guid staffId, Guid boxId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken);
    void AddRange(IEnumerable<ScheduleBlock> scheduleBlock);
}