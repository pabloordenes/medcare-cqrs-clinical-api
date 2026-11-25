using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.GenerateOverbook;

public sealed record GenerateOverbookCommand(
    Guid ResourceId,
    string? Reason = null,
    Guid? CorrelationId = null,
    OverbookDecisionMode DecisionMode = OverbookDecisionMode.Approved,
    decimal? TargetRisk = null) : IRequest<GenerateOverbookResult>;

public enum GenerateOverbookOutcome
{
    Created,
    AlreadyExists,
    AppointmentNotFound,
    AppointmentAlreadyConfirmed,
    AppointmentInvalidState,
    OverbookBlockDeactivated,
    BlockNotFuture,
    BoxInactiveOrAtCapacity,
    DeadlineNotPassed,
    FinalNotificationNotDelivered,
    ClinicalExclusion,
    AutonomousModeDisabled
}

public sealed record GenerateOverbookResult(
    GenerateOverbookOutcome Outcome,
    Guid SourceAppointmentId,
    Guid? OverbookBlockId = null,
    string? ErrorCode = null,
    string Message = "",
    Guid? ReceptionTaskId = null,
    bool IsIdempotentReplay = false,
    OverbookDecisionMode DecisionMode = OverbookDecisionMode.Approved,
    Guid? CorrelationId = null)
{
    public bool IsSuccess => Outcome is GenerateOverbookOutcome.Created or GenerateOverbookOutcome.AlreadyExists;
}
