using MediatR;

namespace MedCareOS.Application.Overbooking.Commands.SeedSyntheticShadowDecisions;

public sealed record SeedSyntheticShadowDecisionsResult(
    Guid TenantId,
    bool AlreadySeeded,
    int SeededCount,
    int FalsePositivesCount,
    decimal FalsePositiveRate,
    bool IsEligibleForAutonomy,
    string SimulationNotice);

public sealed record SeedSyntheticShadowDecisionsCommand(
    Guid? TenantId = null,
    int SampleSize = 220) : IRequest<SeedSyntheticShadowDecisionsResult>;
