namespace MedCareOS.Application.ScheduleBlocks.Queries.GetAllScheduleBlocks;

public record ScheduleBlockDto(
    Guid RuleGroupId,
    Guid StaffId,
    string DoctorFullName,
    string DoctorSpecialty,
    string DoctorRut,
    Guid BoxId,
    string BoxName,
    string BoxFloor,
    DateOnly StartDate,
    DateOnly EndDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    List<DayOfWeek> DaysOfWeek
    );