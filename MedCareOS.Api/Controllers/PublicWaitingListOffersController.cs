using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.WaitingLists.Commands.AcceptWaitingListOffer;
using MedCareOS.Application.WaitingLists.Commands.DeclineWaitingListOffer;
using MedCareOS.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/public/waiting-list/offers")]
[AllowAnonymous]
[EnableRateLimiting("PublicPatientActions")]
public class PublicWaitingListOffersController : ControllerBase
{
    private readonly IPatientActionTokenService _tokenService;
    private readonly ISender _sender;

    public PublicWaitingListOffersController(
        IPatientActionTokenService tokenService,
        ISender sender)
    {
        _tokenService = tokenService;
        _sender = sender;
    }

    public sealed record AcceptOfferByTokenRequestDto(string Token, string? SymptomsRaw = null);
    public sealed record DeclineOfferByTokenRequestDto(string Token, string? Reason = null);

    [HttpGet("preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Preview([FromQuery] string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "Enlace inválido." });

        var preview = await _tokenService.PreviewAsync(token, cancellationToken);
        if (!preview.IsValid)
            return MapTokenFailure(preview.ValidationStatus);

        if (!preview.WaitingListOfferId.HasValue)
            return BadRequest(new { message = "El token no corresponde a una oferta de lista de espera." });

        return Ok(new
        {
            waitingListOfferId = preview.WaitingListOfferId,
            targetKind = preview.TargetKind,
            clinicName = preview.ClinicName,
            appointmentDateLocal = preview.AppointmentDateLocal,
            appointmentTimeLocal = preview.AppointmentTimeLocal,
            specialty = preview.Specialty,
            allowedActions = preview.AllowedActions,
            expiresAt = preview.ExpiresAt
        });
    }

    [HttpPost("accept")]
    [ProducesResponseType(typeof(AcceptWaitingListOfferResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Accept(
        [FromBody] AcceptOfferByTokenRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { message = "Token obligatorio." });

        try
        {
            var result = await _sender.Send(
                new AcceptWaitingListOfferCommand(
                    OfferId: null,
                    RawToken: request.Token,
                    AuthenticatedUserId: null,
                    ExpectedPatientId: null,
                    SymptomsRaw: request.SymptomsRaw,
                    Channel: "public_link"),
                cancellationToken);

            return Ok(result);
        }
        catch (ActionTokenValidationException ex)
        {
            return MapTokenFailure(ex.Status);
        }
        catch (DuplicateAppointmentException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("decline")]
    [ProducesResponseType(typeof(DeclineWaitingListOfferResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Decline(
        [FromBody] DeclineOfferByTokenRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { message = "Token obligatorio." });

        try
        {
            var result = await _sender.Send(
                new DeclineWaitingListOfferCommand(
                    OfferId: null,
                    RawToken: request.Token,
                    AuthenticatedUserId: null,
                    ExpectedPatientId: null,
                    Reason: request.Reason),
                cancellationToken);

            return Ok(result);
        }
        catch (ActionTokenValidationException ex)
        {
            return MapTokenFailure(ex.Status);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    private IActionResult MapTokenFailure(ActionTokenValidationStatus status) => status switch
    {
        ActionTokenValidationStatus.AlreadyUsed or
        ActionTokenValidationStatus.Expired or
        ActionTokenValidationStatus.Revoked or
        ActionTokenValidationStatus.AppointmentNotActionable
            => StatusCode(StatusCodes.Status410Gone, new { message = "La oferta o enlace ya expiró, fue usado o el cupo no está disponible." }),
        _ => BadRequest(new { message = "El enlace proporcionado no es válido." })
    };
}
