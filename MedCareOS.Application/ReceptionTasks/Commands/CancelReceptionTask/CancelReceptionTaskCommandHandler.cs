using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.ReceptionTasks.Commands.CancelReceptionTask;

public class CancelReceptionTaskCommandHandler : IRequestHandler<CancelReceptionTaskCommand>
{
    private readonly IReceptionTaskRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public CancelReceptionTaskCommandHandler(IReceptionTaskRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(CancelReceptionTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _repository.GetByIdAsync(request.TaskId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la tarea de recepción {request.TaskId}.");

        task.Cancel(request.CancelledByUserId, request.Reason);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
