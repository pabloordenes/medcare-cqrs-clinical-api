using MediatR;

namespace MedCareOS.Application.Appointments.Commands.GenerateOverbook;

public record GenerateOverbookCommand(
    Guid ResourceId,
    decimal TargetRisk,
    bool AutoApprove
    ) : IRequest<bool>;