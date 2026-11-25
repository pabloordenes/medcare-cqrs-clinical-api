using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.RescheduleAppointment;

public sealed record RescheduleAppointmentResult(
    Guid AppointmentId,
    Guid PreviousScheduleBlockId,
    Guid NewScheduleBlockId,
    AppointmentStatus Status,
    ConfirmationState ConfirmationState,
    int ReschedulesCount,
    bool SlotReleasedTriggered,
    DateTimeOffset RescheduledAtUtc);

public sealed record RescheduleAppointmentCommand(
    Guid AppointmentId,
    Guid NewScheduleBlockId,
    Guid? AuthenticatedUserId,
    Guid? ExpectedPatientId,
    string? Reason = null,
    string Channel = "patient_portal") : IRequest<RescheduleAppointmentResult>;
