using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.ReceptionTasks.Commands.AssignReceptionTask;

public class AssignReceptionTaskCommandHandler : IRequestHandler<AssignReceptionTaskCommand>
{
    private readonly IReceptionTaskRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public AssignReceptionTaskCommandHandler(IReceptionTaskRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(AssignReceptionTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _repository.GetByIdAsync(request.TaskId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la tarea de recepción {request.TaskId}.");

        task.Assign(request.AssignedToUserId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
