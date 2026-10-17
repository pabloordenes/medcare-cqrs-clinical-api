namespace MedCareOS.Domain.Entities;

public class ScheduleBlock
{
    public Guid Id { get; private set; }
    public Guid StaffId { get; private set; }
    public Guid BoxId { get; private set; }
    public DateTime StartTime { get; private set; }
    public DateTime EndTime { get; private set; }
    
    // propiedades de regla para el panel de admin
    public Guid RuleGroupId { get; private set; }

    public DateOnly RuleStartDate { get; private set; }

    public DateOnly RuleEndDate { get; private set; }

    public TimeOnly RuleStartTime { get; private set; }

    public TimeOnly RuleEndTime { get; private set; }

    public List<DayOfWeek> RuleDaysOfWeek { get; private set; } = [];

    private ScheduleBlock() {} // ef

    private ScheduleBlock(Guid id,
        Guid staffId, Guid boxId,
        DateTime startTime, DateTime endTime,
        Guid ruleGroupId, DateOnly ruleStartDate, DateOnly ruleEndDate,
        TimeOnly ruleStartTime, TimeOnly ruleEndTime, List<DayOfWeek> ruleDaysOfWeek)
    {
        Id = id;
        StaffId = staffId;
        BoxId = boxId;
        StartTime = startTime;
        EndTime = endTime;
        RuleGroupId = ruleGroupId;
        RuleStartDate = ruleStartDate;
        RuleEndDate = ruleEndDate;
        RuleStartTime = ruleStartTime;
        RuleEndTime = ruleEndTime;
        RuleDaysOfWeek = ruleDaysOfWeek;
    }

    public static ScheduleBlock Create(Guid staffId, Guid boxId, DateTime startTime, 
        DateTime endTime, Guid ruleGroupId, DateOnly ruleStartDate, DateOnly ruleEndDate,
        TimeOnly ruleStartTime, TimeOnly ruleEndTime, List<DayOfWeek> ruleDaysOfWeek)
    {
        if (ruleStartDate > ruleEndDate)
            throw new ArgumentException("La fecha de inicio no puede ser posterior a la fecha de fin.");

        if (startTime >= endTime)
            throw new ArgumentException("No puedes terminar un turno antes de empezarlo.");
        
        if (ruleDaysOfWeek == null || !ruleDaysOfWeek.Any())
            throw new ArgumentException("Debes seleccionar al menos un día de la semana.");

        if (staffId == Guid.Empty || boxId == Guid.Empty)
            throw new ArgumentException("Debes seleccionar un doctor/box.");
        
        return new ScheduleBlock(
            Guid.NewGuid(),
            staffId,
            boxId,
            startTime,
            endTime,
            ruleGroupId,
            ruleStartDate,
            ruleEndDate,
            ruleStartTime,
            ruleEndTime,
            ruleDaysOfWeek
            );
    }
}