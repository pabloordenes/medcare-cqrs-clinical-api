using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using MedCareOS.Domain.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Overbooking.Commands.SetTenantAutoOverbook;

public class SetTenantAutoOverbookCommandHandler
    : IRequestHandler<SetTenantAutoOverbookCommand, SetTenantAutoOverbookResult>
{
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly IOverbookShadowDecisionRepository _shadowRepository;
    private readonly IOverbookPolicyAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetTenantAutoOverbookCommandHandler> _logger;

    public SetTenantAutoOverbookCommandHandler(
        ITenantConfirmationPolicyRepository policyRepository,
        IOverbookShadowDecisionRepository shadowRepository,
        IOverbookPolicyAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        ILogger<SetTenantAutoOverbookCommandHandler> logger)
    {
        _policyRepository = policyRepository;
        _shadowRepository = shadowRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<SetTenantAutoOverbookResult> Handle(
        SetTenantAutoOverbookCommand request,
        CancellationToken cancellationToken)
    {
        if (request.AdminUserId == Guid.Empty)
            throw new UnauthorizedAccessException("Usuario administrador no identificado.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("El motivo del cambio de modo autónomo es obligatorio.", nameof(request.Reason));

        var tenantId = request.TenantId ?? RiskThresholds.DefaultTenantId;
        var utcNow = DateTimeOffset.UtcNow;

        var policy = await _policyRepository.GetByTenantIdAsync(tenantId, cancellationToken)
            ?? TenantConfirmationPolicy.CreateDefault(tenantId, utcNow);

        var decisions = await _shadowRepository.GetByTenantAsync(tenantId, isSyntheticOnly: null, cancellationToken);
        var resolvedWouldOverbook = decisions.Where(d => d.WouldHaveOverbooked && d.OutcomeAttendance.HasValue).ToList();
        var falsePositives = resolvedWouldOverbook.Count(d => d.OutcomeAttendance == true);

        var evaluation = OverbookAutonomyEvaluator.Evaluate(resolvedWouldOverbook.Count, falsePositives, policy);

        if (request.Enabled && !evaluation.IsEligibleForAutonomy && !request.AllowDemoOverride)
        {
            throw new InvalidOperationException(
                "No se puede habilitar AutoOverbookEnabled aún: " + string.Join(" ", evaluation.BlockingReasons));
        }

        var previousEnabled = policy.AutoOverbookEnabled;
        var forcedOverride = request.Enabled && !evaluation.IsEligibleForAutonomy && request.AllowDemoOverride;

        policy.SetAutoOverbookEnabled(request.Enabled, utcNow);
        await _policyRepository.UpsertAsync(policy, cancellationToken);

        var auditLog = OverbookPolicyAuditLog.Create(
            tenantId: tenantId,
            changedByUserId: request.AdminUserId,
            previousAutoOverbookEnabled: previousEnabled,
            newAutoOverbookEnabled: policy.AutoOverbookEnabled,
            reason: request.Reason,
            evaluatedCountAtChange: evaluation.EvaluatedWouldOverbookCount,
            falsePositiveRateAtChange: evaluation.PointFalsePositiveRate,
            gatePassed: evaluation.IsEligibleForAutonomy,
            forcedOverride: forcedOverride,
            changedAtUtc: utcNow);

        await _auditLogRepository.AddAsync(auditLog, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Admin {AdminUserId} cambió AutoOverbookEnabled de {Prev} a {Next} en tenant {TenantId} (GatePassed={GatePassed}, ForcedOverride={ForcedOverride}). Motivo: {Reason}",
            request.AdminUserId,
            previousEnabled,
            policy.AutoOverbookEnabled,
            tenantId,
            evaluation.IsEligibleForAutonomy,
            forcedOverride,
            request.Reason.Trim());

        return new SetTenantAutoOverbookResult(
            TenantId: tenantId,
            PreviousAutoOverbookEnabled: previousEnabled,
            NewAutoOverbookEnabled: policy.AutoOverbookEnabled,
            GatePassed: evaluation.IsEligibleForAutonomy,
            ForcedOverride: forcedOverride,
            EvaluatedCountAtChange: evaluation.EvaluatedWouldOverbookCount,
            FalsePositiveRateAtChange: evaluation.PointFalsePositiveRate,
            AuditLogId: auditLog.Id);
    }
}
