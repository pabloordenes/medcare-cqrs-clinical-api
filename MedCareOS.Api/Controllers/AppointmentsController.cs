using MedCareOS.Application.Appointments.Commands.CreateAppointment;
using MedCareOS.Application.Appointments.Commands.FinishConsultation;
using MedCareOS.Application.Appointments.Commands.StartConsultation;
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
    
    // dto simple
    public record UpdateAppointmentStatusRequestDto(string Estado);

    [HttpPatch("{id:guid}/estado")]
    public async Task<IActionResult> UpdateAppointmentStatusRequest([FromRoute] Guid id, 
        [FromBody] UpdateAppointmentStatusRequestDto request, CancellationToken cancellationToken)
    {
        switch (request.Estado.ToLower())
        {
            case "en_curso":
                await _sender.Send(new StartConsultationCommand(id), cancellationToken);
                break;
            case "finalizada":
                await _sender.Send(new FinishConsultationCommand(id), cancellationToken);
                break;
            // todo: agregar el resto de transiciones
            default:
                return BadRequest("El estado no es válido o aún no está soportado.");
        }
        
        return NoContent();
    }
}