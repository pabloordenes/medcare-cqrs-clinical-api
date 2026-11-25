using MassTransit;

namespace MedCareOS.Application.Appointments.Events;

[MessageUrn("medcare:slot-released")]
public sealed record SlotReleased(
    Guid ScheduleBlockId,
    Guid TenantId,
    string Specialty,
    Guid StaffId,
    Guid BoxId,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    Guid VacatedAppointmentId,
    string TriggerReason,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc);
