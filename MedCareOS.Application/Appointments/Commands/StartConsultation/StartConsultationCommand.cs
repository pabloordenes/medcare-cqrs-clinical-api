using MediatR;

namespace MedCareOS.Application.Appointments.Commands.StartConsultation;

public record StartConsultationCommand(
    Guid AppointmentId) : IRequest;