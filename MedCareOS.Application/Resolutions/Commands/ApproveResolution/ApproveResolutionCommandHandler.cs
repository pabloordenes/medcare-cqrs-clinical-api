using MassTransit;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Repositories;
using MediatR;
using Omro.Contracts.Events;

namespace MedCareOS.Application.Resolutions.Commands.ApproveResolution;

public class ApproveResolutionCommandHandler : IRequestHandler<ApproveResolutionCommand>
{
    private readonly IAiResolutionTaskRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublishEndpoint _publishEndpoint;

    public ApproveResolutionCommandHandler(IAiResolutionTaskRepository repository, IUnitOfWork unitOfWork,  IPublishEndpoint publishEndpoint)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _publishEndpoint = publishEndpoint;
    }
    
    public async Task Handle(ApproveResolutionCommand request, CancellationToken cancellationToken)
    {
        var task = await _repository.GetByIdAsync(request.ResolutionTaskId, cancellationToken);
        
        if (task == null) throw new KeyNotFoundException("La tarea de resolución no existe.");

        // mutacion de dominio
        task.Approve(request.ApprovedBy);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        await _publishEndpoint.Publish(
            new ResolutionApproved(
                TenantId: RiskThresholds.DefaultTenantId,
                CorrelationId: Guid.NewGuid(),
                ResolutionTaskId: task.Id,
                ResourceId: task.ResourceId,
                AiPlanJson: task.AiPlanJson),
            cancellationToken);
    }
}