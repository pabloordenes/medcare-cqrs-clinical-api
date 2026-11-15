namespace MedCareOS.Domain.Entities;

public class AiResolutionTask
{
    public Guid Id { get; private set; }
    public Guid ResourceId { get; private set; } // El ID de la Cita original
    public decimal RiskProbability { get; private set; }
    public string AiPlanJson { get; private set; } // El JSON crudo generado por Semantic Kernel
    public string Status { get; private set; } // "Pending", "Approved", "Rejected"
    public DateTimeOffset CreatedAt { get; private set; }
    
    private AiResolutionTask() { } // ef core
    
    public static AiResolutionTask Create(Guid resourceId, decimal riskProbability, string aiPlanJson)
    {
        if (resourceId == Guid.Empty)
            throw new ArgumentException("El ResourceId no puede estar vacío.");

        return new AiResolutionTask
        {
            Id = Guid.NewGuid(),
            ResourceId = resourceId,
            RiskProbability = riskProbability,
            AiPlanJson = aiPlanJson,
            Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
    
    public void Approve() => Status = "Approved";
    public void Reject() => Status = "Rejected";
}