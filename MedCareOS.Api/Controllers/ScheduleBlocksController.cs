using MedCareOS.Application.ScheduleBlocks.Commands.CreateScheduleBlock;
using MedCareOS.Application.ScheduleBlocks.Queries.GetAllScheduleBlocks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScheduleBlocksController : ControllerBase
{
    private readonly ISender _sender;
    
    public ScheduleBlocksController(ISender sender)
    {
        _sender = sender;
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateScheduleBlock([FromBody] CreateScheduleBlockCommand command,
        CancellationToken cancellationToken)
    {
        var ruleGroupId = await _sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new { id = ruleGroupId });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetAllScheduleBlocks(CancellationToken cancellationToken)
    {
        var sblocks = await _sender.Send(new GetAllScheduleBlocksQuery(), cancellationToken);
        return StatusCode(StatusCodes.Status200OK, sblocks);
    }
}