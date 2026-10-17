using MedCareOS.Application.ScheduleBlocks.Commands.CreateScheduleBlock;
using MediatR;
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

    [HttpPost]
    public async Task<IActionResult> CreateScheduleBlock([FromBody] CreateScheduleBlockCommand command,
        CancellationToken cancellationToken)
    {
        var ruleGroupId = await _sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new { id = ruleGroupId });
    }
}