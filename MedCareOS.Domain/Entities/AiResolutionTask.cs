using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class AiResolutionTask
{
    public Guid Id { get; private set; }
    public Guid ResourceId { get; private set; } // El ID de la Cita original
    public decimal RiskProbability { get; private set; }
    public string AiPlanJson { get; private set; } = string.Empty; // El JSON crudo generado por el orquestador
    public AiResolutionStatus Status { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AiResolutionTask() { } // ef core

    public static AiResolutionTask Create(Guid resourceId, decimal riskProbability, string aiPlanJson)
    {
        if (resourceId == Guid.Empty)
            throw new ArgumentException("El ResourceId no puede estar vacío.");

        if (string.IsNullOrWhiteSpace(aiPlanJson))
            throw new ArgumentException("El plan JSON no puede estar vacío.");

        return new AiResolutionTask
        {
            Id = Guid.NewGuid(),
            ResourceId = resourceId,
            RiskProbability = riskProbability,
            AiPlanJson = aiPlanJson,
            Status = AiResolutionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Approve(Guid? approvedBy = null)
    {
        if (Status != AiResolutionStatus.Pending)
        {
            throw new InvalidOperationException(
                $"No se puede aprobar la tarea {Id} porque su estado actual es '{Status}'. Solo tareas en estado 'Pending' pueden ser aprobadas.");
        }

        Status = AiResolutionStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
    }

    public void Reject(Guid? rejectedBy = null)
    {
        if (Status != AiResolutionStatus.Pending)
        {
            throw new InvalidOperationException(
                $"No se puede rechazar la tarea {Id} porque su estado actual es '{Status}'. Solo tareas en estado 'Pending' pueden ser rechazadas.");
        }

        Status = AiResolutionStatus.Rejected;
        ApprovedBy = rejectedBy;
        ApprovedAt = DateTimeOffset.UtcNow;
    }

    public void Supersede()
    {
        if (Status != AiResolutionStatus.Pending)
            throw new InvalidOperationException($"Solo tareas 'Pending' pueden ser reemplazadas (estado actual: '{Status}').");

        Status = AiResolutionStatus.Superseded;
    }

    public void MarkExecuted()
    {
        if (Status != AiResolutionStatus.Approved)
            throw new InvalidOperationException($"Solo tareas 'Approved' pueden marcarse como 'Executed' (estado actual: '{Status}').");

        Status = AiResolutionStatus.Executed;
    }

    public void MarkFailed()
    {
        if (Status != AiResolutionStatus.Approved && Status != AiResolutionStatus.Pending)
            throw new InvalidOperationException($"No se puede marcar como 'Failed' una tarea en estado '{Status}'.");

        Status = AiResolutionStatus.Failed;
    }
}
