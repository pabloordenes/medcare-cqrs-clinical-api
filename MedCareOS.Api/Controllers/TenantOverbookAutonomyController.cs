using System.Security.Claims;
using MedCareOS.Application.Overbooking.Commands.SeedSyntheticShadowDecisions;
using MedCareOS.Application.Overbooking.Commands.SetTenantAutoOverbook;
using MedCareOS.Application.Overbooking.Queries.GetOverbookAutonomyReadiness;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/admin/tenants/overbook-autonomy")]
[Authorize(Policy = "Administrador")]
public class TenantOverbookAutonomyController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IOverbookPolicyAuditLogRepository _auditLogRepository;

    public TenantOverbookAutonomyController(
        ISender sender,
        IOverbookPolicyAuditLogRepository auditLogRepository)
    {
        _sender = sender;
        _auditLogRepository = auditLogRepository;
    }

    public sealed record SetAutoOverbookRequestDto(
        Guid? TenantId,
        bool Enabled,
        string Reason,
        bool AllowDemoOverride = false);

    public sealed record SeedSyntheticRequestDto(
        Guid? TenantId = null,
        int SampleSize = 220);

    [HttpGet("readiness")]
    [ProducesResponseType(typeof(OverbookAutonomyReadinessDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReadiness(
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetOverbookAutonomyReadinessQuery(tenantId), cancellationToken);
        return Ok(result);
    }

    [HttpPatch]
    [ProducesResponseType(typeof(SetTenantAutoOverbookResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetAutoOverbookEnabled(
        [FromBody] SetAutoOverbookRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var adminUserId = GetCurrentUserId();
        if (!adminUserId.HasValue)
            return Forbid();

        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "Reason es obligatorio para habilitar o deshabilitar el modo autónomo." });

        try
        {
            var result = await _sender.Send(
                new SetTenantAutoOverbookCommand(
                    TenantId: request.TenantId,
                    Enabled: request.Enabled,
                    Reason: request.Reason,
                    AdminUserId: adminUserId.Value,
                    AllowDemoOverride: request.AllowDemoOverride),
                cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { message = ex.Message });
        }
    }

    [HttpGet("audit")]
    [ProducesResponseType(typeof(List<OverbookPolicyAuditLog>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedTenantId = tenantId ?? RiskThresholds.DefaultTenantId;
        var logs = await _auditLogRepository.GetByTenantIdAsync(resolvedTenantId, cancellationToken);
        return Ok(logs);
    }

    [HttpPost("seed-synthetic")]
    [ProducesResponseType(typeof(SeedSyntheticShadowDecisionsResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> SeedSynthetic(
        [FromBody] SeedSyntheticRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new SeedSyntheticShadowDecisionsCommand(
                TenantId: request?.TenantId,
                SampleSize: request?.SampleSize ?? 220),
            cancellationToken);
        return Ok(result);
    }

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) && userId != Guid.Empty ? userId : null;
    }
}
