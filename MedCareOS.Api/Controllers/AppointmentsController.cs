using MedCareOS.Application.Appointments.Commands.CreateAppointment;
using MedCareOS.Application.Appointments.Commands.FinishConsultation;
using MedCareOS.Application.Appointments.Commands.GenerateOverbook;
using MedCareOS.Application.Appointments.Commands.MonitorAppointment;
using MedCareOS.Application.Appointments.Commands.StartConsultation;
using MedCareOS.Application.Appointments.Queries.GetDailyAppointments;
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
    
    [Authorize]
    [HttpGet("medico/{medicoId:guid}")]
    public async Task<IActionResult> GetDailyAppointments([FromRoute] Guid medicoId, [FromQuery] DateTime fecha, CancellationToken cancellationToken)
    {
        var appointments = new GetDailyAppointmentsQuery(medicoId, fecha);

        var result = await _sender.Send(appointments, cancellationToken);

        return Ok(result);
    }
    
    public record OverbookPayloadDto(Guid ResourceId, decimal TargetRisk, bool AutoApprove);

    [HttpPost("overbook")]
    public async Task<IActionResult> GenerateOverbookWebhook([FromBody] OverbookPayloadDto payload, CancellationToken cancellationToken)
    {
        var command = new GenerateOverbookCommand(payload.ResourceId, payload.TargetRisk, payload.AutoApprove);
    
        var result = await _sender.Send(command, cancellationToken);

        if (result)
            return Ok(new { message = "Sobrecupo generado exitosamente en MedCareOS." });

        return BadRequest("No se pudo generar el sobrecupo debido a datos inválidos o faltantes.");
    }
    
    public record MonitorPayloadDto(Guid ResourceId, bool RequiresAction);
    
    [HttpPost("monitor")]
    public async Task<IActionResult> MonitorAppointmentWebhook([FromBody] MonitorPayloadDto payload, CancellationToken cancellationToken)
    {
        var command = new MonitorAppointmentCommand(
            payload.ResourceId, 
            payload.RequiresAction);
    
        await _sender.Send(command, cancellationToken);

        return Ok(new { message = "Cita puesta bajo monitoreo manual exitosamente. Notificación generada." });
    }
}