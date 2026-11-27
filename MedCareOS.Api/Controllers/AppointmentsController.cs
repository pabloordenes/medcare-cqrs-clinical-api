using System.Security.Claims;
using MedCareOS.Api.Filters;
using MedCareOS.Application.Appointments.Commands.CancelAppointment;
using MedCareOS.Application.Appointments.Commands.CheckInAppointment;
using MedCareOS.Application.Appointments.Commands.ConfirmAppointment;
using MedCareOS.Application.Appointments.Commands.CreateAppointment;
using MedCareOS.Application.Appointments.Commands.FinishConsultation;
using MedCareOS.Application.Appointments.Commands.GenerateOverbook;
using MedCareOS.Application.Appointments.Commands.MarkNoShowAppointment;
using MedCareOS.Application.Appointments.Commands.MonitorAppointment;
using MedCareOS.Application.Appointments.Commands.OverrideOverbookPriority;
using MedCareOS.Application.Appointments.Commands.StartConsultation;
using MedCareOS.Application.Appointments.Queries.GetDailyAppointments;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        var effectiveCommand = command with { AuthenticatedUserId = null, AllowOverbookBlock = false };

        if (User.IsInRole(nameof(UserRole.Paciente)) ||
            (!User.IsInRole(nameof(UserRole.Recepcionista)) && !User.IsInRole(nameof(UserRole.Administrador))))
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (!Guid.TryParse(sub, out var userId))
                return Forbid();

            effectiveCommand = command with { AuthenticatedUserId = userId, AllowOverbookBlock = false };
        }

        try
        {
            var appointmentId = await _sender.Send(effectiveCommand, cancellationToken);
            return Accepted(appointmentId);
        }
        catch (DuplicateAppointmentException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
                                           ex.InnerException?.Message.Contains("IX_citas_schedule_block_id", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Conflict(new { message = "El bloque horario seleccionado ya se encuentra reservado." });
        }
    }
    
    // dto simple
    public record UpdateAppointmentStatusRequestDto(string Estado, string? Reason = null);

    [Authorize(Roles = "Medico,Recepcionista,Administrador")]
    [HttpPost("{id:guid}/check-in")]
    [HttpPatch("{id:guid}/check-in")]
    public async Task<IActionResult> CheckInAppointment([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _sender.Send(new CheckInAppointmentCommand(id), cancellationToken);
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

    [Authorize(Roles = "Medico,Recepcionista,Administrador")]
    [HttpPatch("{id:guid}/estado")]
    public async Task<IActionResult> UpdateAppointmentStatusRequest([FromRoute] Guid id,
        [FromBody] UpdateAppointmentStatusRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            switch (request.Estado.Trim().ToLowerInvariant())
            {
                case "confirmada":
                    await _sender.Send(new ConfirmAppointmentCommand(id), cancellationToken);
                    break;
                case "check_in":
                    await _sender.Send(new CheckInAppointmentCommand(id), cancellationToken);
                    break;
                case "en_curso":
                    await _sender.Send(new StartConsultationCommand(id), cancellationToken);
                    break;
                case "finalizada":
                    await _sender.Send(new FinishConsultationCommand(id), cancellationToken);
                    break;
                case "cancelada":
                    await _sender.Send(new CancelAppointmentCommand(id, request.Reason), cancellationToken);
                    break;
                case "no_asistio":
                    await _sender.Send(new MarkNoShowAppointmentCommand(id), cancellationToken);
                    break;
                default:
                    return BadRequest("El estado no es válido o aún no está soportado.");
            }

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
    
    [Authorize]
    [HttpGet("medico/{medicoId:guid}")]
    public async Task<IActionResult> GetDailyAppointments([FromRoute] Guid medicoId, [FromQuery] DateTime fecha, CancellationToken cancellationToken)
    {
        var appointments = new GetDailyAppointmentsQuery(medicoId, fecha);

        var result = await _sender.Send(appointments, cancellationToken);

        return Ok(result);
    }
    
    public record OverbookPayloadDto(
        Guid ResourceId,
        string? Reason = null,
        Guid? CorrelationId = null,
        OverbookDecisionMode? DecisionMode = null,
        decimal? TargetRisk = null,
        bool? AutoApprove = null);

    [Authorize(Policy = "OmroService")]
    [TypeFilter(typeof(IdempotencyKeyFilter))]
    [HttpPost("overbook")]
    public async Task<IActionResult> GenerateOverbookWebhook([FromBody] OverbookPayloadDto payload, CancellationToken cancellationToken)
    {
        var resolvedMode = payload.DecisionMode ??
            (payload.AutoApprove == true ? OverbookDecisionMode.Autonomous : OverbookDecisionMode.Approved);

        var command = new GenerateOverbookCommand(
            ResourceId: payload.ResourceId,
            Reason: payload.Reason,
            CorrelationId: payload.CorrelationId,
            DecisionMode: resolvedMode,
            TargetRisk: payload.TargetRisk);

        var result = await _sender.Send(command, cancellationToken);

        return result.Outcome switch
        {
            GenerateOverbookOutcome.Created or GenerateOverbookOutcome.AlreadyExists => Ok(new
            {
                status = result.Outcome == GenerateOverbookOutcome.Created ? "Created" : "AlreadyExists",
                overbookBlockId = result.OverbookBlockId,
                sourceAppointmentId = result.SourceAppointmentId,
                decisionMode = result.DecisionMode,
                idempotentReplay = result.IsIdempotentReplay,
                correlationId = result.CorrelationId,
                message = result.Message
            }),
            GenerateOverbookOutcome.AppointmentNotFound => NotFound(new
            {
                code = result.ErrorCode,
                message = result.Message,
                resourceId = result.SourceAppointmentId
            }),
            GenerateOverbookOutcome.AppointmentAlreadyConfirmed
                or GenerateOverbookOutcome.AppointmentInvalidState
                or GenerateOverbookOutcome.OverbookBlockDeactivated => Conflict(new
            {
                code = result.ErrorCode,
                message = result.Message,
                resourceId = result.SourceAppointmentId
            }),
            _ => UnprocessableEntity(new
            {
                code = result.ErrorCode,
                message = result.Message,
                resourceId = result.SourceAppointmentId,
                receptionTaskId = result.ReceptionTaskId
            })
        };
    }
    
    public record MonitorPayloadDto(
        Guid ResourceId,
        bool RequiresAction,
        string? Reason = null,
        ReceptionTaskPriority? Priority = null);
    
    [Authorize(Policy = "OmroService")]
    [TypeFilter(typeof(IdempotencyKeyFilter))]
    [HttpPost("monitor")]
    public async Task<IActionResult> MonitorAppointmentWebhook([FromBody] MonitorPayloadDto payload, CancellationToken cancellationToken)
    {
        var command = new MonitorAppointmentCommand(
            payload.ResourceId,
            payload.RequiresAction,
            payload.Reason,
            payload.Priority ?? ReceptionTaskPriority.High);

        try
        {
            await _sender.Send(command, cancellationToken);
            return Ok(new { message = "Cita puesta bajo monitoreo manual exitosamente. Tarea de recepción generada." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    public record OverrideOverbookCollisionDto(Guid WinnerAppointmentId, string Reason);

    [Authorize(Roles = "Recepcionista,Administrador")]
    [HttpPost("{id:guid}/overbook-collision/override")]
    public async Task<IActionResult> OverrideOverbookCollision(
        [FromRoute] Guid id,
        [FromBody] OverrideOverbookCollisionDto payload,
        CancellationToken cancellationToken)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId) || userId == Guid.Empty)
            return Forbid();

        if (payload is null)
            return BadRequest(new { message = "Solicitud inválida." });

        try
        {
            var result = await _sender.Send(
                new OverrideOverbookPriorityCommand(id, payload.WinnerAppointmentId, payload.Reason, userId),
                cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
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
}