using MedCareOS.Application.Staffs.Commands.CreateStaff;
using MedCareOS.Application.Staffs.Queries.GetAllStaff;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MedCareOS.Application.Staffs.Commands.UpdateStaff;

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

    [Authorize]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateStaff([FromRoute] Guid id, [FromBody] UpdateStaffCommand command, CancellationToken cancellationToken)
    {
        if(command.StaffId != id)
           return BadRequest("El ID de la ruta no coincide con el ID enviado en el body.");

        await _sender.Send(command, cancellationToken);

        return NoContent();
    }


    
}