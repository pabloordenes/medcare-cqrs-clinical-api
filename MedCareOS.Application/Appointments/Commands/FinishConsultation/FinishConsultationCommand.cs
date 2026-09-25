using MediatR;

namespace MedCareOS.Application.Appointments.Commands.FinishConsultation;

public record FinishConsultationCommand(
    Guid AppointmentId) : IRequest;