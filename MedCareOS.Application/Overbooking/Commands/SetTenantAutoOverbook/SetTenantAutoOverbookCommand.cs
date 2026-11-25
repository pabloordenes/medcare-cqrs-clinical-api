using MediatR;

namespace MedCareOS.Application.Overbooking.Commands.SetTenantAutoOverbook;

public sealed record SetTenantAutoOverbookResult(
    Guid TenantId,
    bool PreviousAutoOverbookEnabled,
    bool NewAutoOverbookEnabled,
    bool GatePassed,
    bool ForcedOverride,
    int EvaluatedCountAtChange,
    decimal FalsePositiveRateAtChange,
    Guid AuditLogId);

public sealed record SetTenantAutoOverbookCommand(
    Guid? TenantId,
    bool Enabled,
    string Reason,
    Guid AdminUserId,
    bool AllowDemoOverride = false) : IRequest<SetTenantAutoOverbookResult>;
