namespace Omro.Domain.ResolutionTasks; // urn & exchange rabbitMQ

public readonly record struct AnomalyEvent
(
    decimal RiskProbability,
    Guid ResourceId
);