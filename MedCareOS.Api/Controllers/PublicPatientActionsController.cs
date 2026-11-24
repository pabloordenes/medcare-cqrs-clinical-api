using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Notifications.Commands.ProcessPatientReply;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/public/appointments")]
[AllowAnonymous]
[EnableRateLimiting("PublicPatientActions")]
public class PublicPatientActionsController : ControllerBase
{
    private readonly IPatientActionTokenService _tokenService;
    private readonly INotificationRepository _notificationRepository;
    private readonly ISender _sender;

    public PublicPatientActionsController(
        IPatientActionTokenService tokenService,
        INotificationRepository notificationRepository,
        ISender sender)
    {
        _tokenService = tokenService;
        _notificationRepository = notificationRepository;
        _sender = sender;
    }

    public record PublicPatientActionRequestDto(string Token, PatientReplyIntent Action);

    [HttpGet("action-preview")]
    public async Task<IActionResult> PreviewAction([FromQuery] string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "Enlace inválido." });

        var preview = await _tokenService.PreviewAsync(token, cancellationToken);
        if (!preview.IsValid)
        {
            return MapValidationFailure(preview.ValidationStatus);
        }

        return Ok(new
        {
            clinicName = preview.ClinicName,
            appointmentDateLocal = preview.AppointmentDateLocal,
            appointmentTimeLocal = preview.AppointmentTimeLocal,
            specialty = preview.Specialty,
            allowedActions = preview.AllowedActions,
            expiresAt = preview.ExpiresAt
        });
    }

    [HttpPost("action")]
    public async Task<IActionResult> ExecuteAction(
        [FromBody] PublicPatientActionRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token) ||
            request.Action is not PatientReplyIntent.Confirm and not PatientReplyIntent.Cancel and not PatientReplyIntent.OptOut)
        {
            return BadRequest(new { message = "Solicitud inválida." });
        }

        var consumed = await _tokenService.ConsumeAsync(request.Token, request.Action, cancellationToken);
        if (!consumed.IsSuccess)
        {
            return MapValidationFailure(consumed.ValidationStatus);
        }

        var sourceChannel = NotificationChannel.Email;
        if (consumed.NotificationId.HasValue)
        {
            var notif = await _notificationRepository.GetByIdAsync(consumed.NotificationId.Value, cancellationToken);
            if (notif is not null)
                sourceChannel = notif.Channel;
        }

        await _sender.Send(
            new ProcessPatientReplyCommand(
                AppointmentId: consumed.AppointmentId!.Value,
                PatientId: consumed.PatientId!.Value,
                NotificationId: consumed.NotificationId,
                Intent: consumed.ExecutedIntent,
                Channel: sourceChannel,
                NormalizedKeyword: $"LINK_{consumed.ExecutedIntent.ToString().ToUpperInvariant()}",
                ReceivedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        return Ok(new
        {
            message = "Acción registrada exitosamente.",
            action = consumed.ExecutedIntent,
            appointmentId = consumed.AppointmentId
        });
    }

    private IActionResult MapValidationFailure(ActionTokenValidationStatus status) => status switch
    {
        ActionTokenValidationStatus.AlreadyUsed or
        ActionTokenValidationStatus.Expired or
        ActionTokenValidationStatus.Revoked or
        ActionTokenValidationStatus.AppointmentNotActionable
            => StatusCode(StatusCodes.Status410Gone, new { message = "Este enlace ya expiró, fue utilizado o la cita ya no está disponible." }),
        _ => BadRequest(new { message = "El enlace proporcionado no es válido." })
    };
}
