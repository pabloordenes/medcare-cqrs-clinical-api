using MediatR;

namespace MedCareOS.Application.ReceptionTasks.Commands.CancelReceptionTask;

public record CancelReceptionTaskCommand(
    Guid TaskId,
    Guid CancelledByUserId,
    string? Reason = null) : IRequest;
