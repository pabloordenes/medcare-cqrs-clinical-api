using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.ReceptionTasks.Commands.CompleteReceptionTask;

public class CompleteReceptionTaskCommandHandler : IRequestHandler<CompleteReceptionTaskCommand>
{
    private readonly IReceptionTaskRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public CompleteReceptionTaskCommandHandler(IReceptionTaskRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(CompleteReceptionTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _repository.GetByIdAsync(request.TaskId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la tarea de recepción {request.TaskId}.");

        task.Complete(request.ResolvedByUserId, request.ResolutionNotes);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
