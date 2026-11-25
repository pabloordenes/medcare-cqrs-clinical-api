using MedCareOS.Application.Appointments.Commands.ConfirmAppointment;
using MedCareOS.Application.Appointments.Commands.DeclineAppointment;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Patients.Commands.RecordPatientOptOut;
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
    public record PublicTokenConfirmRequestDto(string Token);
    public record PublicTokenDeclineRequestDto(string Token, string? Reason = null);

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
            expiresAt = preview.ExpiresAt,
            waitingListOfferId = preview.WaitingListOfferId,
            targetKind = preview.TargetKind
        });
    }

    [HttpPost("action")]
    public Task<IActionResult> ExecuteAction(
        [FromBody] PublicPatientActionRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Token) ||
            request.Action is not PatientReplyIntent.Confirm and not PatientReplyIntent.Cancel and not PatientReplyIntent.OptOut)
        {
            return Task.FromResult<IActionResult>(BadRequest(new { message = "Solicitud inválida." }));
        }

        return ConsumeAndDispatchAsync(request.Token, request.Action, declineReason: null, cancellationToken);
    }

    [HttpPost("confirm")]
    public Task<IActionResult> ConfirmByToken(
        [FromBody] PublicTokenConfirmRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Token))
            return Task.FromResult<IActionResult>(BadRequest(new { message = "Solicitud inválida." }));

        return ConsumeAndDispatchAsync(request.Token, PatientReplyIntent.Confirm, declineReason: null, cancellationToken);
    }

    [HttpPost("decline")]
    public Task<IActionResult> DeclineByToken(
        [FromBody] PublicTokenDeclineRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Token))
            return Task.FromResult<IActionResult>(BadRequest(new { message = "Solicitud inválida." }));

        return ConsumeAndDispatchAsync(request.Token, PatientReplyIntent.Cancel, request.Reason, cancellationToken);
    }

    private async Task<IActionResult> ConsumeAndDispatchAsync(
        string token,
        PatientReplyIntent intent,
        string? declineReason,
        CancellationToken cancellationToken)
    {
        var consumed = await _tokenService.ConsumeAsync(token, intent, cancellationToken);
        if (!consumed.IsSuccess)
        {
            return MapValidationFailure(consumed.ValidationStatus);
        }

        var sourceChannel = NotificationChannel.Email;
        Guid? correlationId = null;

        if (consumed.NotificationId.HasValue)
        {
            var notif = await _notificationRepository.GetByIdAsync(consumed.NotificationId.Value, cancellationToken);
            if (notif is not null)
            {
                sourceChannel = notif.Channel;
                correlationId = notif.CorrelationId;
            }
        }

        var utcNow = DateTimeOffset.UtcNow;
        var keyword = $"LINK_{consumed.ExecutedIntent.ToString().ToUpperInvariant()}";

        try
        {
            switch (consumed.ExecutedIntent)
            {
                case PatientReplyIntent.Confirm:
                {
                    var result = await _sender.Send(
                        new ConfirmAppointmentCommand(
                            AppointmentId: consumed.AppointmentId!.Value,
                            ExpectedPatientId: consumed.PatientId!.Value,
                            Channel: sourceChannel,
                            NotificationId: consumed.NotificationId,
                            Source: "public_token",
                            NormalizedKeyword: keyword,
                            OccurredAtUtc: utcNow,
                            CorrelationId: correlationId,
                            PublishPatientReplyEvent: true),
                        cancellationToken);

                    return Ok(new
                    {
                        message = "Acción registrada exitosamente.",
                        action = consumed.ExecutedIntent,
                        appointmentId = consumed.AppointmentId,
                        outcome = result.Outcome,
                        confirmationState = result.ConfirmationState,
                        appointmentStatus = result.AppointmentStatus,
                        wasIdempotentReplay = result.WasIdempotentReplay,
                        effectiveRiskScore = result.EffectiveRiskScore,
                        effectiveBand = result.EffectiveBand
                    });
                }

                case PatientReplyIntent.Cancel:
                {
                    var result = await _sender.Send(
                        new DeclineAppointmentCommand(
                            AppointmentId: consumed.AppointmentId!.Value,
                            ExpectedPatientId: consumed.PatientId!.Value,
                            Channel: sourceChannel,
                            NotificationId: consumed.NotificationId,
                            Source: "public_token",
                            Reason: string.IsNullOrWhiteSpace(declineReason)
                                ? $"Declinada por paciente vía {sourceChannel}."
                                : declineReason.Trim(),
                            NormalizedKeyword: keyword,
                            OccurredAtUtc: utcNow,
                            CorrelationId: correlationId,
                            PublishPatientReplyEvent: true),
                        cancellationToken);

                    return Ok(new
                    {
                        message = "Acción registrada exitosamente.",
                        action = consumed.ExecutedIntent,
                        appointmentId = consumed.AppointmentId,
                        outcome = result.Outcome,
                        confirmationState = result.ConfirmationState,
                        appointmentStatus = result.AppointmentStatus,
                        wasIdempotentReplay = result.WasIdempotentReplay
                    });
                }

                case PatientReplyIntent.OptOut:
                {
                    await _sender.Send(
                        new RecordPatientOptOutCommand(
                            PatientId: consumed.PatientId!.Value,
                            AuthenticatedUserId: null,
                            Source: $"{sourceChannel.ToString().ToLowerInvariant()}_link"),
                        cancellationToken);

                    return Ok(new
                    {
                        message = "Acción registrada exitosamente.",
                        action = consumed.ExecutedIntent,
                        appointmentId = consumed.AppointmentId
                    });
                }

                default:
                    return BadRequest(new { message = "Solicitud inválida." });
            }
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
