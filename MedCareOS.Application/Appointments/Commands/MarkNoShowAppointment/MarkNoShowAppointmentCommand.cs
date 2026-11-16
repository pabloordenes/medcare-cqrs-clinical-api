using MediatR;

namespace MedCareOS.Application.Appointments.Commands.MarkNoShowAppointment;

public record MarkNoShowAppointmentCommand(Guid AppointmentId) : IRequest;
