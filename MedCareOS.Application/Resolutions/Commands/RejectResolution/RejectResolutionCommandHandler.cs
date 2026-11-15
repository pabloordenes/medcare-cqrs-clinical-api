using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Resolutions.Commands.RejectResolution;

public class RejectResolutionCommandHandler : IRequestHandler<RejectResolutionCommand>
{
    private readonly IAiResolutionTaskRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    
    public RejectResolutionCommandHandler(IAiResolutionTaskRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }
    
    public async Task Handle(RejectResolutionCommand request, CancellationToken cancellationToken)
    {
        var task = await _repository.GetByIdAsync(request.ResolutionTaskId, cancellationToken);
        
        if (task == null)
            throw new KeyNotFoundException("La tarea de resolución no existe.");

        // mutacion de dominio
        task.Reject(); 

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}