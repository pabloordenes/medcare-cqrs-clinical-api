namespace MedCareOS.Domain.Entities;

public class ScheduleBlock
{
    public Guid Id { get; private set; }
    public Guid StaffId { get; private set; }
    public Guid BoxId { get; private set; }
    public DateTime StartTime { get; private set; }
    public DateTime EndTime { get; private set; }

    public bool IsOverbook { get; private set; }
    public Guid? SourceAppointmentId { get; private set; }

    // propiedades de regla para el panel de admin (nulas cuando es sobrecupo)
    public Guid? RuleGroupId { get; private set; }
    public DateOnly? RuleStartDate { get; private set; }
    public DateOnly? RuleEndDate { get; private set; }
    public TimeOnly? RuleStartTime { get; private set; }
    public TimeOnly? RuleEndTime { get; private set; }
    public List<DayOfWeek>? RuleDaysOfWeek { get; private set; }

    private ScheduleBlock() { } // ef

    private ScheduleBlock(
        Guid id,
        Guid staffId,
        Guid boxId,
        DateTime startTime,
        DateTime endTime,
        bool isOverbook,
        Guid? sourceAppointmentId,
        Guid? ruleGroupId,
        DateOnly? ruleStartDate,
        DateOnly? ruleEndDate,
        TimeOnly? ruleStartTime,
        TimeOnly? ruleEndTime,
        List<DayOfWeek>? ruleDaysOfWeek)
    {
        Id = id;
        StaffId = staffId;
        BoxId = boxId;
        StartTime = startTime;
        EndTime = endTime;
        IsOverbook = isOverbook;
        SourceAppointmentId = sourceAppointmentId;
        RuleGroupId = ruleGroupId;
        RuleStartDate = ruleStartDate;
        RuleEndDate = ruleEndDate;
        RuleStartTime = ruleStartTime;
        RuleEndTime = ruleEndTime;
        RuleDaysOfWeek = ruleDaysOfWeek;
    }

    public static ScheduleBlock Create(
        Guid staffId,
        Guid boxId,
        DateTime startTime,
        DateTime endTime,
        Guid ruleGroupId,
        DateOnly ruleStartDate,
        DateOnly ruleEndDate,
        TimeOnly ruleStartTime,
        TimeOnly ruleEndTime,
        List<DayOfWeek> ruleDaysOfWeek)
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
            isOverbook: false,
            sourceAppointmentId: null,
            ruleGroupId,
            ruleStartDate,
            ruleEndDate,
            ruleStartTime,
            ruleEndTime,
            ruleDaysOfWeek);
    }

    public static ScheduleBlock CreateOverbook(ScheduleBlock source, Guid sourceAppointmentId)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (sourceAppointmentId == Guid.Empty)
            throw new ArgumentException("El SourceAppointmentId no puede estar vacío.", nameof(sourceAppointmentId));

        if (source.IsOverbook)
            throw new InvalidOperationException("No se puede crear un sobrecupo a partir de otro bloque de sobrecupo.");

        return new ScheduleBlock(
            Guid.NewGuid(),
            source.StaffId,
            source.BoxId,
            source.StartTime,
            source.EndTime,
            isOverbook: true,
            sourceAppointmentId: sourceAppointmentId,
            ruleGroupId: null,
            ruleStartDate: null,
            ruleEndDate: null,
            ruleStartTime: null,
            ruleEndTime: null,
            ruleDaysOfWeek: null);
    }
}
