using System.Security.Claims;
using MedCareOS.Application.Appointments.Commands.CancelAppointment;
using MedCareOS.Application.Appointments.Commands.RescheduleAppointment;
using MedCareOS.Application.Appointments.Queries.GetMyAppointments;
using MedCareOS.Application.Appointments.Queries.GetRescheduleOptions;
using MedCareOS.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/patients/me/appointments")]
[Authorize(Policy = "Paciente")]
public class PatientAppointmentsController : ControllerBase
{
    private readonly ISender _sender;

    public PatientAppointmentsController(ISender sender)
    {
        _sender = sender;
    }

    public sealed record PatientCancelAppointmentRequestDto(string? Reason = null);
    public sealed record PatientRescheduleAppointmentRequestDto(Guid NewScheduleBlockId, string? Reason = null);

    [HttpGet]
    [ProducesResponseType(typeof(List<PatientAppointmentSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyAppointments(
        [FromQuery] bool includePast = false,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Forbid();

        try
        {
            var items = await _sender.Send(new GetMyAppointmentsQuery(userId.Value, includePast), cancellationToken);
            return Ok(items);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelMyAppointment(
        [FromRoute] Guid id,
        [FromBody] PatientCancelAppointmentRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Forbid();

        try
        {
            await _sender.Send(
                new CancelAppointmentCommand(
                    AppointmentId: id,
                    Reason: request?.Reason ?? "Cancelada por el paciente en autoservicio.",
                    AuthenticatedUserId: userId.Value),
                cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
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

    [HttpGet("{id:guid}/reschedule-options")]
    [ProducesResponseType(typeof(RescheduleOptionsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetRescheduleOptions(
        [FromRoute] Guid id,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        [FromQuery] Guid? preferredStaffId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Forbid();

        try
        {
            var result = await _sender.Send(
                new GetRescheduleOptionsQuery(
                    AppointmentId: id,
                    AuthenticatedUserId: userId.Value,
                    ExpectedPatientId: null,
                    FromDate: fromDate,
                    ToDate: toDate,
                    PreferredStaffId: preferredStaffId),
                cancellationToken);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
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

    [HttpPost("{id:guid}/reschedule")]
    [ProducesResponseType(typeof(RescheduleAppointmentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RescheduleMyAppointment(
        [FromRoute] Guid id,
        [FromBody] PatientRescheduleAppointmentRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Forbid();

        if (request is null || request.NewScheduleBlockId == Guid.Empty)
            return BadRequest(new { message = "NewScheduleBlockId es obligatorio." });

        try
        {
            var result = await _sender.Send(
                new RescheduleAppointmentCommand(
                    AppointmentId: id,
                    NewScheduleBlockId: request.NewScheduleBlockId,
                    AuthenticatedUserId: userId.Value,
                    ExpectedPatientId: null,
                    Reason: request.Reason,
                    Channel: "patient_portal"),
                cancellationToken);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (DuplicateAppointmentException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
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
