using MediatR;

namespace MedCareOS.Application.Appointments.Commands.ConfirmAppointment;

public record ConfirmAppointmentCommand(Guid AppointmentId) : IRequest;
