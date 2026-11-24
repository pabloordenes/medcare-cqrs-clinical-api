using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.MonitorAppointment;

public record MonitorAppointmentCommand(
    Guid ResourceId,
    bool RequiresAction,
    string? Reason = null,
    ReceptionTaskPriority Priority = ReceptionTaskPriority.High
) : IRequest;
