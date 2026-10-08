using MedCareOS.Application.Staffs.Commands.CreateStaff;
using MedCareOS.Application.Staffs.Queries.GetAllStaff;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StaffController : ControllerBase
{
    private readonly ISender _sender;

    public StaffController(ISender sender)
    {
        _sender = sender;
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateStaff([FromBody] CreateStaffCommand command, CancellationToken cancellationToken)
    {
        var staffId = await _sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new { id = staffId });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetAllStaff(CancellationToken cancellationToken)
    {
        var staffs = await _sender.Send(new GetAllStaffQuery(), cancellationToken);
        return Ok(staffs);
    }
    
}