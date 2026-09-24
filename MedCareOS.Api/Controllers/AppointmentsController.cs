using MedCareOS.Application.Appointments.Commands.CreateAppointment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly ISender _sender;
    
    public AppointmentsController(ISender sender)
    {
        _sender = sender;
    }
    
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        var appointmentId = await _sender.Send(command, cancellationToken);
        return Ok(appointmentId);
    }
}