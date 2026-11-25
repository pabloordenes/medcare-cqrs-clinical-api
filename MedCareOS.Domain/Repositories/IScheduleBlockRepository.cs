using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IScheduleBlockRepository
{
    Task<List<ScheduleBlock>> GetAssignedBlocksInRangeAsync(Guid staffId, Guid boxId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken);
    void AddRange(IEnumerable<ScheduleBlock> scheduleBlock);
    Task<ScheduleBlock?> GetByIdAsync(Guid scheduleBlockId, CancellationToken cancellationToken);
    Task<ScheduleBlock?> GetByIdForUpdateAsync(Guid scheduleBlockId, CancellationToken cancellationToken = default);
    Task<ScheduleBlock?> GetOverbookByAppointmentIdAsync(Guid sourceAppointmentId, CancellationToken cancellationToken = default);
    Task<ScheduleBlock?> GetOverbookByAppointmentIdTrackedAsync(Guid sourceAppointmentId, CancellationToken cancellationToken = default);
    Task<ScheduleBlock?> GetOverbookByAppointmentIdForUpdateAsync(Guid sourceAppointmentId, CancellationToken cancellationToken = default);
    Task<int> CountActiveOverbooksForBoxSlotAsync(Guid boxId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default);
}
