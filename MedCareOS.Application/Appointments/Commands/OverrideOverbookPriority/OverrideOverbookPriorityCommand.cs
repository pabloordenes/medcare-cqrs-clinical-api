using MedCareOS.Domain.Services;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.OverrideOverbookPriority;

public sealed record OverrideOverbookPriorityCommand(
    Guid AppointmentId,
    Guid WinnerAppointmentId,
    string Reason,
    Guid OverriddenByUserId) : IRequest<OverrideOverbookPriorityResult>;

public sealed record OverrideOverbookPriorityResult(
    Guid OriginalAppointmentId,
    Guid OverbookAppointmentId,
    Guid OverbookBlockId,
    OverbookWinner Winner,
    Guid WinnerAppointmentId,
    Guid DisplacedAppointmentId,
    Guid AuditEventId);
