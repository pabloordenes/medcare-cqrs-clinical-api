using System.Security.Claims;
using MedCareOS.Application.ReceptionTasks.Commands.AssignReceptionTask;
using MedCareOS.Application.ReceptionTasks.Commands.CancelReceptionTask;
using MedCareOS.Application.ReceptionTasks.Commands.CompleteReceptionTask;
using MedCareOS.Application.ReceptionTasks.Queries.GetReceptionTasks;
using MedCareOS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/reception-tasks")]
[Authorize(Policy = "Recepcionista")]
public class ReceptionTasksController : ControllerBase
{
    private readonly ISender _sender;

    public ReceptionTasksController(ISender sender)
    {
        _sender = sender;
    }

    public record AssignReceptionTaskRequestDto(Guid? AssignedToUserId = null);
    public record CompleteReceptionTaskRequestDto(string? ResolutionNotes = null);
    public record CancelReceptionTaskRequestDto(string? Reason = null);

    [HttpGet]
    public async Task<IActionResult> GetReceptionTasks(
        [FromQuery] ReceptionTaskStatus? status = null,
        [FromQuery] ReceptionTaskPriority? priority = null,
        [FromQuery] bool assignedToMe = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        Guid? assignedToUserId = null;
        if (assignedToMe)
        {
            assignedToUserId = GetCurrentUserId();
            if (!assignedToUserId.HasValue)
                return Forbid();
        }

        var result = await _sender.Send(
            new GetReceptionTasksQuery(status, priority, assignedToUserId, page, pageSize),
            cancellationToken);

        return Ok(result);
    }

    [HttpPatch("{id:guid}/assign")]
    public async Task<IActionResult> AssignTask(
        [FromRoute] Guid id,
        [FromBody] AssignReceptionTaskRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = request?.AssignedToUserId is { } explicitUserId && explicitUserId != Guid.Empty
            ? explicitUserId
            : GetCurrentUserId();

        if (!targetUserId.HasValue || targetUserId.Value == Guid.Empty)
            return Forbid();

        try
        {
            await _sender.Send(new AssignReceptionTaskCommand(id, targetUserId.Value), cancellationToken);
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

    [HttpPatch("{id:guid}/complete")]
    public async Task<IActionResult> CompleteTask(
        [FromRoute] Guid id,
        [FromBody] CompleteReceptionTaskRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Forbid();

        try
        {
            await _sender.Send(new CompleteReceptionTaskCommand(id, userId.Value, request?.ResolutionNotes), cancellationToken);
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

    [HttpPatch("{id:guid}/cancel")]
    public async Task<IActionResult> CancelTask(
        [FromRoute] Guid id,
        [FromBody] CancelReceptionTaskRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Forbid();

        try
        {
            await _sender.Send(new CancelReceptionTaskCommand(id, userId.Value, request?.Reason), cancellationToken);
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
        return Guid.TryParse(sub, out var userId) && userId != Guid.Empty ? userId : null;
    }
}
