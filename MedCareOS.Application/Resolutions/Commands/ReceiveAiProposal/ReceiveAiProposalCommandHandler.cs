using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Resolutions.Commands.ReceiveAiProposal;

public class ReceiveAiProposalCommandHandler : IRequestHandler<ReceiveAiProposalCommand, Guid>
{
    private readonly IAiResolutionTaskRepository _repository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReceiveAiProposalCommandHandler> _logger;
    
    public ReceiveAiProposalCommandHandler(
        IAiResolutionTaskRepository repository,
        IAppointmentRepository appointmentRepository,
        IUnitOfWork unitOfWork,
        ILogger<ReceiveAiProposalCommandHandler> logger)
    {
        _repository = repository;
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }
    
    public async Task<Guid> Handle(ReceiveAiProposalCommand request, CancellationToken cancellationToken)
    {
        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.ResourceId, cancellationToken);
        if (appointment is not null &&
            (appointment.OrchestrationSuppressed ||
             appointment.IsPatientConfirmed ||
             appointment.Status != AppointmentStatus.Scheduled))
        {
            var stalePending = await _repository.GetAllPendingByResourceIdAsync(request.ResourceId, cancellationToken);
            foreach (var staleTask in stalePending)
            {
                staleTask.Supersede();
            }

            var suppressedTask = AiResolutionTask.Create(
                request.ResourceId,
                request.RiskProbability,
                request.AiPlanJson);
            suppressedTask.Supersede();

            _repository.Add(suppressedTask);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Propuesta HITL {TaskId} para cita {ResourceId} registrada directamente como Superseded porque la cita ya fue confirmada/suprimida (Status={Status}, ConfirmationState={ConfirmationState}).",
                suppressedTask.Id,
                request.ResourceId,
                appointment.Status,
                appointment.ConfirmationState);

            return suppressedTask.Id;
        }

        var existingPending = await _repository.GetPendingByResourceIdAsync(request.ResourceId, cancellationToken);
        if (existingPending != null)
        {
            _logger.LogInformation(
                "Propuesta duplicada ignorada (idempotente) para recurso {ResourceId}. Retornando tarea existente {TaskId}.",
                request.ResourceId, existingPending.Id);
            return existingPending.Id;
        }

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