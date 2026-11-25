using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using MedCareOS.Api.Filters;
using MedCareOS.Application.Resolutions.Commands.ApproveResolution;
using MedCareOS.Application.Resolutions.Commands.ReceiveAiProposal;
using MedCareOS.Application.Resolutions.Commands.RejectResolution;
using MedCareOS.Application.Resolutions.Queries.GetPendingResolutions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omro.Contracts.Plans;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResolutionsController : ControllerBase
{
    private readonly ISender _sender;
    
    public ResolutionsController(ISender sender)
    {
        _sender = sender;
    }
    
    public record ResolutionProposalDto(
        Guid ResourceId,
        decimal RiskProbability,
        string? AiPlanJson = null,
        JsonElement? AiPlan = null,
        string? SchemaVersion = null,
        DateTimeOffset? ProposalExpiresAt = null);
    
    [Authorize(Policy = "OmroService")]
    [TypeFilter(typeof(IdempotencyKeyFilter))]
    [HttpPost("propose")]
    public async Task<IActionResult> ReceiveAiProposal([FromBody] ResolutionProposalDto payload, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(payload.SchemaVersion) &&
            !PlanContractVersions.IsSupported(payload.SchemaVersion))
        {
            return BadRequest(new { message = $"SchemaVersion '{payload.SchemaVersion}' no soportada." });
        }

        string resolvedAiPlanJson;

        if (payload.AiPlan.HasValue && payload.AiPlan.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (payload.AiPlan.Value.ValueKind != JsonValueKind.Object)
            {
                return BadRequest(new { message = "El campo 'aiPlan' debe ser un objeto JSON." });
            }

            var aiPlanNode = JsonNode.Parse(payload.AiPlan.Value.GetRawText())?.AsObject();
            if (aiPlanNode is null)
            {
                return BadRequest(new { message = "El campo 'aiPlan' debe ser un objeto JSON." });
            }

            if (TryGetPropertyIgnoreCase(aiPlanNode, "schemaVersion", out var nestedVersionNode) &&
                nestedVersionNode is JsonValue versionVal &&
                versionVal.TryGetValue<string>(out var nestedVersion) &&
                !string.IsNullOrWhiteSpace(nestedVersion) &&
                !PlanContractVersions.IsSupported(nestedVersion))
            {
                return BadRequest(new { message = $"SchemaVersion '{nestedVersion}' en 'aiPlan' no soportada." });
            }

            if (payload.ProposalExpiresAt.HasValue &&
                (!TryGetPropertyIgnoreCase(aiPlanNode, "proposalExpiresAt", out var existingExpiry) ||
                 existingExpiry is null))
            {
                aiPlanNode["proposalExpiresAt"] = payload.ProposalExpiresAt.Value.ToString("O", CultureInfo.InvariantCulture);
            }

            resolvedAiPlanJson = aiPlanNode.ToJsonString();
        }
        else if (!string.IsNullOrWhiteSpace(payload.AiPlanJson))
        {
            try
            {
                using var _ = JsonDocument.Parse(payload.AiPlanJson);
            }
            catch (JsonException)
            {
                return BadRequest(new { message = "El campo 'aiPlanJson' no contiene JSON válido." });
            }

            resolvedAiPlanJson = payload.AiPlanJson;
        }
        else
        {
            return BadRequest(new { message = "Debe proporcionar 'aiPlan' (v2) o 'aiPlanJson' (v1)." });
        }

        var command = new ReceiveAiProposalCommand(
            payload.ResourceId,
            payload.RiskProbability,
            resolvedAiPlanJson,
            payload.ProposalExpiresAt);
        
        var taskId = await _sender.Send(command, cancellationToken);
        
        // todo: inyectar IHubContext aquí para emitir alerta vía WebSockets (SignalR) al Frontend
        
        return Ok(new { message = "Propuesta de IA recibida exitosamente", taskId });
    }

    private static bool TryGetPropertyIgnoreCase(JsonObject obj, string propertyName, out JsonNode? value)
    {
        if (obj.TryGetPropertyValue(propertyName, out value))
            return true;

        foreach (var kvp in obj)
        {
            if (string.Equals(kvp.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
    
    [Authorize(Roles = "Administrador,Recepcionista")]
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingResolutions(CancellationToken cancellationToken)
    {
        var pendingTasks = await _sender.Send(new GetPendingResolutionsQuery(), cancellationToken);
        
        return Ok(pendingTasks);
    }
    
    [Authorize(Roles = "Administrador,Recepcionista")]
    [HttpPatch("{id:guid}/approve")]
    public async Task<IActionResult> ApproveResolution(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        try
        {
            await _sender.Send(new ApproveResolutionCommand(id, userId), cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [Authorize(Roles = "Administrador,Recepcionista")]
    [HttpPatch("{id:guid}/reject")]
    public async Task<IActionResult> RejectResolution(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        try
        {
            await _sender.Send(new RejectResolutionCommand(id, userId), cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }
}

public record ResolutionProposalDto(
    Guid ResolutionTaskId,
    Guid ResourceId,
    decimal RiskProbability,
    string AiPlan
);