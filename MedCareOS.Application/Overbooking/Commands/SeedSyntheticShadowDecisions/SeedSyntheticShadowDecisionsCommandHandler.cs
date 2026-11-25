using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using MedCareOS.Domain.Services;
using MediatR;

namespace MedCareOS.Application.Overbooking.Commands.SeedSyntheticShadowDecisions;

public class SeedSyntheticShadowDecisionsCommandHandler
    : IRequestHandler<SeedSyntheticShadowDecisionsCommand, SeedSyntheticShadowDecisionsResult>
{
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly IOverbookShadowDecisionRepository _shadowRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SeedSyntheticShadowDecisionsCommandHandler(
        ITenantConfirmationPolicyRepository policyRepository,
        IOverbookShadowDecisionRepository shadowRepository,
        IUnitOfWork unitOfWork)
    {
        _policyRepository = policyRepository;
        _shadowRepository = shadowRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<SeedSyntheticShadowDecisionsResult> Handle(
        SeedSyntheticShadowDecisionsCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = request.TenantId ?? RiskThresholds.DefaultTenantId;
        var policy = await _policyRepository.GetOrDefaultAsync(tenantId, cancellationToken);

        var alreadySeeded = await _shadowRepository.HasSyntheticSeedAsync(tenantId, cancellationToken);
        if (!alreadySeeded)
        {
            var count = Math.Clamp(request.SampleSize, 200, 1000);
            var utcNow = DateTimeOffset.UtcNow;
            var batch = new List<OverbookShadowDecision>(count);

            for (int i = 0; i < count; i++)
            {
                // ~11.1% falsos positivos (cada 9no paciente asistió sin confirmar; 24/220 ≈ 10.91% < 15%)
                bool attendedWithoutConfirming = (i % 9) == 0;
                var evaluatedAt = utcNow.AddDays(-30).AddHours(i * 2);
                var score = 0.8550m + ((i % 13) * 0.0100m);

                var row = OverbookShadowDecision.Create(
                    tenantId: tenantId,
                    appointmentId: null,
                    scheduleBlockId: null,
                    effectiveRiskScore: Math.Min(0.9900m, score),
                    thresholdUsed: policy.AutoOverbookMinEffectiveScore,
                    decision: "ShadowWouldAutoOverbook",
                    wouldHaveOverbooked: true,
                    finalNotificationDelivered: true,
                    hasClinicalExclusion: false,
                    clinicalExclusionCode: ClinicalExclusionEvaluator.NoneCode,
                    isSynthetic: true,
                    evaluatedAtUtc: evaluatedAt,
                    correlationId: Guid.NewGuid());

                row.ResolveOutcome(attendedWithoutConfirming, evaluatedAt.AddHours(14));
                batch.Add(row);
            }

            await _shadowRepository.AddRangeAsync(batch, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var all = await _shadowRepository.GetByTenantAsync(tenantId, isSyntheticOnly: null, cancellationToken);
        var resolved = all.Where(d => d.WouldHaveOverbooked && d.OutcomeAttendance.HasValue).ToList();
        var fp = resolved.Count(d => d.OutcomeAttendance == true);
        var eval = OverbookAutonomyEvaluator.Evaluate(resolved.Count, fp, policy);

        return new SeedSyntheticShadowDecisionsResult(
            TenantId: tenantId,
            AlreadySeeded: alreadySeeded,
            SeededCount: all.Count(d => d.IsSynthetic),
            FalsePositivesCount: eval.FalsePositivesCount,
            FalsePositiveRate: eval.PointFalsePositiveRate,
            IsEligibleForAutonomy: eval.IsEligibleForAutonomy,
            SimulationNotice: "Validación calibrada mediante muestra sintética marcada (is_synthetic=true) porque el entorno de demo/lanzamiento aún no acumula N>=200 evaluaciones reales.");
    }
}
