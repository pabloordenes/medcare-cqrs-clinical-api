using MassTransit;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Omro.Contracts.Events;

namespace MedCareOS.Application.Resolutions.Commands.ApproveResolution;

public class ApproveResolutionCommandHandler : IRequestHandler<ApproveResolutionCommand>
{
    private readonly IAiResolutionTaskRepository _repository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublishEndpoint _publishEndpoint;

    public ApproveResolutionCommandHandler(
        IAiResolutionTaskRepository repository,
        IAppointmentRepository appointmentRepository,
        IUnitOfWork unitOfWork,
        IPublishEndpoint publishEndpoint)
    {
        _repository = repository;
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
        _publishEndpoint = publishEndpoint;
    }
    
    public async Task Handle(ApproveResolutionCommand request, CancellationToken cancellationToken)
    {
        var task = await _repository.GetByIdAsync(request.ResolutionTaskId, cancellationToken);
        
        if (task == null) throw new KeyNotFoundException("La tarea de resolución no existe.");

        if (task.Status == AiResolutionStatus.Pending)
        {
            var appointment = await _appointmentRepository.GetAppointmentByIdAsync(task.ResourceId, cancellationToken);
            if (appointment is not null &&
                (appointment.OrchestrationSuppressed ||
                 appointment.IsPatientConfirmed ||
                 appointment.Status != AppointmentStatus.Scheduled))
            {
                task.Supersede();
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"La tarea {task.Id} fue reemplazada (Superseded) porque la cita {appointment.Id} ya fue confirmada o dejó de estar activa.");
            }
        }

        // mutacion de dominio
        task.Approve(request.ApprovedBy);

        await _publishEndpoint.Publish(
            new ResolutionApproved(
                TenantId: RiskThresholds.DefaultTenantId,
                CorrelationId: Guid.NewGuid(),
                ResolutionTaskId: task.Id,
                ResourceId: task.ResourceId,
                AiPlanJson: task.AiPlanJson),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}