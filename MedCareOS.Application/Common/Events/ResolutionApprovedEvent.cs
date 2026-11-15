namespace Omro.Domain.ResolutionTasks; // debe coincidir el namespace con omro

public record ResolutionApprovedEvent(Guid ResolutionTaskId, string AiPlanJson);