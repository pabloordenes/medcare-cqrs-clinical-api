using MediatR;

namespace MedCareOS.Application.Appointments.Commands.CancelAppointment;

public record CancelAppointmentCommand(Guid AppointmentId, string? Reason = null) : IRequest;
