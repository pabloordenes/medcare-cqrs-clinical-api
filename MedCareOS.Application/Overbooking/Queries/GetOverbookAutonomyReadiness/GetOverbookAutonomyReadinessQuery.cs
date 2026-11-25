using MediatR;

namespace MedCareOS.Application.Overbooking.Queries.GetOverbookAutonomyReadiness;

public sealed record OverbookAutonomyReadinessDto(
    Guid TenantId,
    bool AutoOverbookEnabled,
    bool IsEligibleForAutonomy,
    int EvaluatedWouldOverbookCount,
    int FalsePositivesCount,
    int TruePositivesCount,
    int UnresolvedShadowCount,
    int SyntheticEvaluatedCount,
    int RealEvaluatedCount,
    bool IncludesSyntheticData,
    int MinimumRequiredEvaluations,
    decimal MaxAllowedFalsePositiveRate,
    decimal PointFalsePositiveRate,
    decimal WilsonUpperBound95,
    IReadOnlyList<string> BlockingReasons);

public sealed record GetOverbookAutonomyReadinessQuery(Guid? TenantId = null)
    : IRequest<OverbookAutonomyReadinessDto>;
