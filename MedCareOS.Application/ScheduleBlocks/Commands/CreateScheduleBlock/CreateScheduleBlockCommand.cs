using MediatR;

namespace MedCareOS.Application.ScheduleBlocks.Commands.CreateScheduleBlock;

public record CreateScheduleBlockCommand(
    Guid StaffId,
    Guid BoxId,
    DateOnly StartDate,
    DateOnly EndDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    List<DayOfWeek> DaysOfWeek
    ) : IRequest<Guid>;