using MediatR;

namespace MedCareOS.Application.Appointments.Commands.CheckInAppointment;

public record CheckInAppointmentCommand(Guid AppointmentId) : IRequest;
