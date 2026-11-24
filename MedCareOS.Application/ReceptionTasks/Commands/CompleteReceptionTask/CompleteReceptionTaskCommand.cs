using MediatR;

namespace MedCareOS.Application.ReceptionTasks.Commands.CompleteReceptionTask;

public record CompleteReceptionTaskCommand(
    Guid TaskId,
    Guid ResolvedByUserId,
    string? ResolutionNotes = null) : IRequest;
