using MediatR;

namespace MedCareOS.Application.Appointments.Commands.MonitorAppointment;

public record MonitorAppointmentCommand(
    Guid ResourceId, 
    bool RequiresAction
    ) : IRequest;