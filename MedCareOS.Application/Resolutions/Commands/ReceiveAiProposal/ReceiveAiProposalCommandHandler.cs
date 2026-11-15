using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Resolutions.Commands.ReceiveAiProposal;

public class ReceiveAiProposalCommandHandler : IRequestHandler<ReceiveAiProposalCommand, Guid>
{
    private readonly IAiResolutionTaskRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReceiveAiProposalCommandHandler> _logger;
    
    public ReceiveAiProposalCommandHandler(
        IAiResolutionTaskRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<ReceiveAiProposalCommandHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }
    
    public async Task<Guid> Handle(ReceiveAiProposalCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Ingresando nueva propuesta de la IA (HITL) para el recurso {ResourceId} con riesgo {Risk}", 
            request.ResourceId, request.RiskProbability);

        var resolutionTask = AiResolutionTask.Create(
            request.ResourceId, 
            request.RiskProbability, 
            request.AiPlanJson
        );

        _repository.Add(resolutionTask);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Propuesta de la IA guardada exitosamente con ID {TaskId} esperando revisión humana.", 
            resolutionTask.Id);

        return resolutionTask.Id;
    }
}