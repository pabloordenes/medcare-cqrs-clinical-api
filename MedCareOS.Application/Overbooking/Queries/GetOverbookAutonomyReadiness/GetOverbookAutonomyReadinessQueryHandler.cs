using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Repositories;
using MedCareOS.Domain.Services;
using MediatR;

namespace MedCareOS.Application.Overbooking.Queries.GetOverbookAutonomyReadiness;

public class GetOverbookAutonomyReadinessQueryHandler
    : IRequestHandler<GetOverbookAutonomyReadinessQuery, OverbookAutonomyReadinessDto>
{
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly IOverbookShadowDecisionRepository _shadowDecisionRepository;

    public GetOverbookAutonomyReadinessQueryHandler(
        ITenantConfirmationPolicyRepository policyRepository,
        IOverbookShadowDecisionRepository shadowDecisionRepository)
    {
        _policyRepository = policyRepository;
        _shadowDecisionRepository = shadowDecisionRepository;
    }

    public async Task<OverbookAutonomyReadinessDto> Handle(
        GetOverbookAutonomyReadinessQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = request.TenantId ?? RiskThresholds.DefaultTenantId;
        var policy = await _policyRepository.GetOrDefaultAsync(tenantId, cancellationToken);
        var decisions = await _shadowDecisionRepository.GetByTenantAsync(tenantId, isSyntheticOnly: null, cancellationToken);

        var resolvedWouldOverbook = decisions
            .Where(d => d.WouldHaveOverbooked && d.OutcomeAttendance.HasValue)
            .ToList();

        var totalWouldOverbook = resolvedWouldOverbook.Count;
        var falsePositives = resolvedWouldOverbook.Count(d => d.OutcomeAttendance == true);
        var unresolvedCount = decisions.Count(d => d.WouldHaveOverbooked && !d.OutcomeAttendance.HasValue);
        var syntheticCount = decisions.Count(d => d.IsSynthetic);
        var realCount = decisions.Count(d => !d.IsSynthetic);

        var evaluation = OverbookAutonomyEvaluator.Evaluate(totalWouldOverbook, falsePositives, policy);

        return new OverbookAutonomyReadinessDto(
            TenantId: tenantId,
            AutoOverbookEnabled: policy.AutoOverbookEnabled,
            IsEligibleForAutonomy: evaluation.IsEligibleForAutonomy,
            EvaluatedWouldOverbookCount: evaluation.EvaluatedWouldOverbookCount,
            FalsePositivesCount: evaluation.FalsePositivesCount,
            TruePositivesCount: evaluation.TruePositivesCount,
            UnresolvedShadowCount: unresolvedCount,
            SyntheticEvaluatedCount: syntheticCount,
            RealEvaluatedCount: realCount,
            IncludesSyntheticData: syntheticCount > 0,
            MinimumRequiredEvaluations: evaluation.MinimumRequiredEvaluations,
            MaxAllowedFalsePositiveRate: evaluation.MaxAllowedFalsePositiveRate,
            PointFalsePositiveRate: evaluation.PointFalsePositiveRate,
            WilsonUpperBound95: evaluation.WilsonUpperBound95,
            BlockingReasons: evaluation.BlockingReasons);
    }
}
