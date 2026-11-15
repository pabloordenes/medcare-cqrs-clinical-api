namespace MedCareOS.Application.Resolutions.Queries.GetPendingResolutions;

public record ResolutionTaskDto(
    Guid Id,
    Guid ResourceId,
    decimal RiskProbability,
    string AiPlanJson,
    string Status,
    DateTimeOffset CreatedAt
    );