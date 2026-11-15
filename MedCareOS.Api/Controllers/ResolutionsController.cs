using MedCareOS.Application.Resolutions.Commands.ReceiveAiProposal;
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
    
    [HttpPost("propose")]
    public async Task<IActionResult> ReceiveAiProposal([FromBody] ResolutionProposalDto payload, CancellationToken cancellationToken)
    {
        var command = new ReceiveAiProposalCommand(payload.ResourceId, payload.RiskProbability, payload.AiPlanJson);
        
        var taskId = await _sender.Send(command, cancellationToken);
        
        // todo: inyectar IHubContext aquí para emitir alerta vía WebSockets (SignalR) al Frontend
        
        return Ok(new { message = "Propuesta de IA recibida exitosamente", taskId });
    }
    
    [Authorize]
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingResolutions(CancellationToken cancellationToken)
    {
        var pendingTasks = await _sender.Send(new GetPendingResolutionsQuery(), cancellationToken);
        
        return Ok(pendingTasks);
    }
}

public record ResolutionProposalDto(
    Guid ResolutionTaskId,
    Guid ResourceId,
    decimal RiskProbability,
    string AiPlan
);