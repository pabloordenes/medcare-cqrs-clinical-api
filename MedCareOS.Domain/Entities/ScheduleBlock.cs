using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class ScheduleBlock
{
    public const int MaxDeactivationReasonLength = 300;

    public Guid Id { get; private set; }
    public Guid StaffId { get; private set; }
    public Guid BoxId { get; private set; }
    public DateTime StartTime { get; private set; }
    public DateTime EndTime { get; private set; }

    public bool IsOverbook { get; private set; }
    public Guid? SourceAppointmentId { get; private set; }
    public OverbookBlockStatus OverbookStatus { get; private set; } = OverbookBlockStatus.None;
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public string? DeactivationReason { get; private set; }
    public Guid? OccupiedAppointmentId { get; private set; }
    public Guid ConcurrencyStamp { get; private set; } = Guid.NewGuid();
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActiveOverbook =>
        IsOverbook && OverbookStatus is OverbookBlockStatus.Open or OverbookBlockStatus.Offered or OverbookBlockStatus.Occupied;

    public bool IsAvailableForBackfillOffer =>
        !IsOverbook || OverbookStatus is OverbookBlockStatus.Open or OverbookBlockStatus.Offered;

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
        OverbookBlockStatus overbookStatus,
        Guid? ruleGroupId,
        DateOnly? ruleStartDate,
        DateOnly? ruleEndDate,
        TimeOnly? ruleStartTime,
        TimeOnly? ruleEndTime,
        List<DayOfWeek>? ruleDaysOfWeek,
        DateTimeOffset utcNow)
    {
        Id = id;
        StaffId = staffId;
        BoxId = boxId;
        StartTime = startTime;
        EndTime = endTime;
        IsOverbook = isOverbook;
        SourceAppointmentId = sourceAppointmentId;
        OverbookStatus = overbookStatus;
        RuleGroupId = ruleGroupId;
        RuleStartDate = ruleStartDate;
        RuleEndDate = ruleEndDate;
        RuleStartTime = ruleStartTime;
        RuleEndTime = ruleEndTime;
        RuleDaysOfWeek = ruleDaysOfWeek;
        ConcurrencyStamp = Guid.NewGuid();
        UpdatedAt = utcNow;
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
            overbookStatus: OverbookBlockStatus.None,
            ruleGroupId,
            ruleStartDate,
            ruleEndDate,
            ruleStartTime,
            ruleEndTime,
            ruleDaysOfWeek,
            DateTimeOffset.UtcNow);
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
            overbookStatus: OverbookBlockStatus.Open,
            ruleGroupId: null,
            ruleStartDate: null,
            ruleEndDate: null,
            ruleStartTime: null,
            ruleEndTime: null,
            ruleDaysOfWeek: null,
            DateTimeOffset.UtcNow);
    }

    public void MarkOverbookOffered(DateTimeOffset utcNow)
    {
        EnsureOverbook();
        if (OverbookStatus is not (OverbookBlockStatus.Open or OverbookBlockStatus.Offered))
            throw new InvalidOperationException($"No se puede ofertar un sobrecupo en estado '{OverbookStatus}'.");

        OverbookStatus = OverbookBlockStatus.Offered;
        ConcurrencyStamp = Guid.NewGuid();
        UpdatedAt = utcNow;
    }

    public void RevertOverbookToOpen(DateTimeOffset utcNow)
    {
        EnsureOverbook();
        if (OverbookStatus != OverbookBlockStatus.Offered)
            throw new InvalidOperationException($"Solo un sobrecupo en estado 'Offered' puede volver a 'Open' (actual: '{OverbookStatus}').");

        OverbookStatus = OverbookBlockStatus.Open;
        ConcurrencyStamp = Guid.NewGuid();
        UpdatedAt = utcNow;
    }

    public void MarkOverbookOccupied(Guid appointmentId, DateTimeOffset utcNow)
    {
        EnsureOverbook();
        if (appointmentId == Guid.Empty)
            throw new ArgumentException("El AppointmentId que ocupa el sobrecupo no puede estar vacío.", nameof(appointmentId));

        if (OverbookStatus is not (OverbookBlockStatus.Open or OverbookBlockStatus.Offered))
            throw new InvalidOperationException($"No se puede ocupar un sobrecupo en estado '{OverbookStatus}'.");

        OverbookStatus = OverbookBlockStatus.Occupied;
        OccupiedAppointmentId = appointmentId;
        ConcurrencyStamp = Guid.NewGuid();
        UpdatedAt = utcNow;
    }

    public bool DeactivateOverbook(string reason, DateTimeOffset utcNow)
    {
        EnsureOverbook();

        if (OverbookStatus == OverbookBlockStatus.Deactivated)
            return false;

        if (OverbookStatus == OverbookBlockStatus.Occupied)
            throw new InvalidOperationException("No se puede desactivar un bloque de sobrecupo que ya fue ocupado.");

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "OverbookDeactivated" : reason.Trim();
        DeactivationReason = normalizedReason.Length <= MaxDeactivationReasonLength
            ? normalizedReason
            : normalizedReason[..MaxDeactivationReasonLength];
        DeactivatedAt = utcNow;
        OverbookStatus = OverbookBlockStatus.Deactivated;
        ConcurrencyStamp = Guid.NewGuid();
        UpdatedAt = utcNow;
        return true;
    }

    public void ExpireOverbook(DateTimeOffset utcNow)
    {
        EnsureOverbook();
        if (OverbookStatus == OverbookBlockStatus.Expired)
            return;

        if (OverbookStatus is not (OverbookBlockStatus.Open or OverbookBlockStatus.Offered))
            throw new InvalidOperationException($"Solo un sobrecupo en estado 'Open' o 'Offered' puede expirar (actual: '{OverbookStatus}').");

        OverbookStatus = OverbookBlockStatus.Expired;
        ConcurrencyStamp = Guid.NewGuid();
        UpdatedAt = utcNow;
    }

    private void EnsureOverbook()
    {
        if (!IsOverbook)
            throw new InvalidOperationException("Esta operación solo aplica a bloques de sobrecupo.");
    }
}
