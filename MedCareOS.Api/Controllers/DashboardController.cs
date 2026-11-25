using MedCareOS.Application.Dashboard.Queries.GetMetrics;
using MedCareOS.Application.Overbooking.Queries.GetOverbookMetrics;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly ISender _sender;

    public DashboardController(ISender sender)
    {
        _sender = sender;
    }
    
    [Authorize]
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(CancellationToken cancellationToken)
    {
        var metrics = await _sender.Send(new GetDashboardMetricsQuery(), cancellationToken);
        return Ok(metrics);
    }

    [Authorize]
    [HttpGet("overbook-metrics")]
    [ProducesResponseType(typeof(OverbookMetricsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverbookMetrics(
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var metrics = await _sender.Send(new GetOverbookMetricsQuery(tenantId), cancellationToken);
        return Ok(metrics);
    }
}