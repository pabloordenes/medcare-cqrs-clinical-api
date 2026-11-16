using System.Security.Claims;
using MedCareOS.Api.Filters;
using MedCareOS.Application.Resolutions.Commands.ApproveResolution;
using MedCareOS.Application.Resolutions.Commands.ReceiveAiProposal;
using MedCareOS.Application.Resolutions.Commands.RejectResolution;
using MedCareOS.Application.Resolutions.Queries.GetPendingResolutions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    
    public record ResolutionProposalDto(Guid ResourceId, decimal RiskProbability, string AiPlanJson);
    
    [Authorize(Policy = "OmroService")]
    [TypeFilter(typeof(IdempotencyKeyFilter))]
    [HttpPost("propose")]
    public async Task<IActionResult> ReceiveAiProposal([FromBody] ResolutionProposalDto payload, CancellationToken cancellationToken)
    {
        var command = new ReceiveAiProposalCommand(payload.ResourceId, payload.RiskProbability, payload.AiPlanJson);
        
        var taskId = await _sender.Send(command, cancellationToken);
        
        // todo: inyectar IHubContext aquí para emitir alerta vía WebSockets (SignalR) al Frontend
        
        return Ok(new { message = "Propuesta de IA recibida exitosamente", taskId });
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