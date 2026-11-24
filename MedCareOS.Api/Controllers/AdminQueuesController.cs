using MedCareOS.Infrastructure.Messaging.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/admin/queues")]
[Authorize(Policy = "Administrador")]
public class AdminQueuesController : ControllerBase
{
    private readonly IRabbitMqQueueAdminService _queueAdminService;

    public AdminQueuesController(IRabbitMqQueueAdminService queueAdminService)
    {
        _queueAdminService = queueAdminService;
    }

    [HttpGet("errors")]
    public async Task<IActionResult> GetErrorQueues(CancellationToken cancellationToken)
    {
        var summaries = await _queueAdminService.GetErrorQueueSummariesAsync(cancellationToken);
        return Ok(summaries);
    }

    [HttpPost("{queueName}/requeue")]
    public async Task<IActionResult> RequeueErrorQueue(
        [FromRoute] string queueName,
        [FromQuery] int maxMessages = 50,
        CancellationToken cancellationToken = default)
    {
        if (maxMessages is < 1 or > 500)
            return BadRequest(new { message = "maxMessages debe estar entre 1 y 500." });

        try
        {
            var result = await _queueAdminService.RequeueErrorMessagesAsync(queueName, maxMessages, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message, allowedQueues = _queueAdminService.AllowedQueues });
        }
    }
}
