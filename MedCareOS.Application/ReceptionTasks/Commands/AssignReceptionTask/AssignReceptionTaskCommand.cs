using MediatR;

namespace MedCareOS.Application.ReceptionTasks.Commands.AssignReceptionTask;

public record AssignReceptionTaskCommand(Guid TaskId, Guid AssignedToUserId) : IRequest;
