using System.Security.Claims;
using MedCareOS.Application.Patients.Commands.CreatePatient;
using MedCareOS.Application.Patients.Commands.RecordPatientOptOut;
using MedCareOS.Application.Patients.Commands.UpdatePatientNotificationPreferences;
using MedCareOS.Application.Patients.Queries.GetPatientNotificationPreferences;
using MedCareOS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    private readonly ISender _sender;

    public PatientsController(ISender sender)
    {
        _sender = sender;
    }

    public record UpdateNotificationPreferencesRequestDto(
        NotificationChannel PreferredChannel,
        bool WhatsAppOptIn,
        string? Source = null);

    public record OptOutRequestDto(string? Source = null);

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreatePatient([FromBody] CreatePatientCommand command, CancellationToken cancellationToken)
    {
        var patientId = await _sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new { id = patientId });
    }

    [Authorize(Policy = "Paciente")]
    [HttpGet("me/notification-preferences")]
    public async Task<IActionResult> GetMyNotificationPreferences(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Forbid();

        try
        {
            var result = await _sender.Send(
                new GetPatientNotificationPreferencesQuery(AuthenticatedUserId: userId.Value),
                cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [Authorize(Policy = "Paciente")]
    [HttpPut("me/notification-preferences")]
    public async Task<IActionResult> UpdateMyNotificationPreferences(
        [FromBody] UpdateNotificationPreferencesRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Forbid();

        try
        {
            var source = string.IsNullOrWhiteSpace(request.Source) ? "patient_portal" : request.Source;
            var result = await _sender.Send(
                new UpdatePatientNotificationPreferencesCommand(
                    PatientId: null,
                    AuthenticatedUserId: userId.Value,
                    PreferredChannel: request.PreferredChannel,
                    WhatsAppOptIn: request.WhatsAppOptIn,
                    Source: source),
                cancellationToken);
            return Ok(result);
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

    [Authorize(Policy = "Paciente")]
    [HttpPost("me/opt-out")]
    public async Task<IActionResult> OptOutSelf(
        [FromBody] OptOutRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
            return Forbid();

        try
        {
            var source = string.IsNullOrWhiteSpace(request?.Source) ? "patient_portal" : request!.Source;
            await _sender.Send(
                new RecordPatientOptOutCommand(PatientId: null, AuthenticatedUserId: userId.Value, Source: source),
                cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [Authorize(Policy = "Recepcionista")]
    [HttpGet("{id:guid}/notification-preferences")]
    public async Task<IActionResult> GetPatientNotificationPreferences(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sender.Send(
                new GetPatientNotificationPreferencesQuery(PatientId: id),
                cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [Authorize(Policy = "Recepcionista")]
    [HttpPut("{id:guid}/notification-preferences")]
    public async Task<IActionResult> UpdatePatientNotificationPreferences(
        [FromRoute] Guid id,
        [FromBody] UpdateNotificationPreferencesRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = string.IsNullOrWhiteSpace(request.Source) ? "reception_desk" : request.Source;
            var result = await _sender.Send(
                new UpdatePatientNotificationPreferencesCommand(
                    PatientId: id,
                    AuthenticatedUserId: null,
                    PreferredChannel: request.PreferredChannel,
                    WhatsAppOptIn: request.WhatsAppOptIn,
                    Source: source),
                cancellationToken);
            return Ok(result);
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

    [Authorize(Policy = "Recepcionista")]
    [HttpPost("{id:guid}/opt-out")]
    public async Task<IActionResult> OptOutPatient(
        [FromRoute] Guid id,
        [FromBody] OptOutRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var source = string.IsNullOrWhiteSpace(request?.Source) ? "reception_desk" : request!.Source;
            await _sender.Send(
                new RecordPatientOptOutCommand(PatientId: id, AuthenticatedUserId: null, Source: source),
                cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) && userId != Guid.Empty ? userId : null;
    }
}
