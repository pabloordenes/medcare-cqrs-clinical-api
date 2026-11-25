using System.Security.Claims;
using MedCareOS.Application.WaitingLists.Commands.AcceptWaitingListOffer;
using MedCareOS.Application.WaitingLists.Commands.DeclineWaitingListOffer;
using MedCareOS.Application.WaitingLists.Commands.JoinWaitingList;
using MedCareOS.Application.WaitingLists.Commands.LeaveWaitingList;
using MedCareOS.Application.WaitingLists.Queries;
using MedCareOS.Application.WaitingLists.Queries.GetMyWaitingList;
using MedCareOS.Application.WaitingLists.Queries.GetMyWaitingListOffers;
using MedCareOS.Application.WaitingLists.Queries.GetWaitingListCandidates;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedCareOS.Api.Controllers;

[ApiController]
[Route("api/waiting-list")]
public class WaitingListController : ControllerBase
{
    private readonly ISender _sender;

    public WaitingListController(ISender sender)
    {
        _sender = sender;
    }

    public sealed record JoinWaitingListRequestDto(
        Guid? PatientId,
        string Specialty,
        int? Priority = null,
        string? ClinicalContextJson = null,
        Guid? PreferredStaffId = null,
        DateOnly? PreferredFromDate = null,
        DateOnly? PreferredToDate = null,
        TimeOnly? PreferredTimeStart = null,
        TimeOnly? PreferredTimeEnd = null,
        Guid? DisplacedSourceAppointmentId = null,
        DateTimeOffset? ExpiresAt = null,
        Guid? TenantId = null);

    public sealed record JoinWaitingListResponseDto(Guid Id);
    public sealed record AcceptOfferRequestDto(string? SymptomsRaw = null);
    public sealed record DeclineOfferRequestDto(string? Reason = null);

    [Authorize(Policy = "Paciente")]
    [HttpPost]
    [ProducesResponseType(typeof(JoinWaitingListResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> JoinWaitingList(
        [FromBody] JoinWaitingListRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var isStaff = IsReceptionOrAdmin();
        var currentUserId = GetCurrentUserId();

        if (!isStaff && !currentUserId.HasValue)
            return Forbid();

        if (isStaff && (!request.PatientId.HasValue || request.PatientId.Value == Guid.Empty))
            return BadRequest(new { message = "PatientId es obligatorio para registros de recepción o administración." });

        try
        {
            var id = await _sender.Send(
                new JoinWaitingListCommand(
                    PatientId: isStaff ? request.PatientId : null,
                    AuthenticatedUserId: isStaff ? null : currentUserId,
                    Specialty: request.Specialty,
                    Priority: isStaff ? (request.Priority ?? 0) : 0,
                    ClinicalContextJson: request.ClinicalContextJson,
                    PreferredStaffId: request.PreferredStaffId,
                    PreferredFromDate: request.PreferredFromDate,
                    PreferredToDate: request.PreferredToDate,
                    PreferredTimeStart: request.PreferredTimeStart,
                    PreferredTimeEnd: request.PreferredTimeEnd,
                    DisplacedSourceAppointmentId: request.DisplacedSourceAppointmentId,
                    ExpiresAt: request.ExpiresAt,
                    TenantId: request.TenantId),
                cancellationToken);

            return StatusCode(StatusCodes.Status201Created, new JoinWaitingListResponseDto(id));
        }
        catch (DuplicateWaitingListException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
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
    [HttpGet("me")]
    [ProducesResponseType(typeof(List<WaitingListEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyWaitingList(CancellationToken cancellationToken = default)
    {
        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
            return Forbid();

        try
        {
            var entries = await _sender.Send(new GetMyWaitingListQuery(currentUserId.Value), cancellationToken);
            return Ok(entries);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [Authorize(Policy = "Paciente")]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LeaveWaitingList(
        [FromRoute] Guid id,
        [FromQuery] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var isStaff = IsReceptionOrAdmin();
        var currentUserId = GetCurrentUserId();

        if (!isStaff && !currentUserId.HasValue)
            return Forbid();

        try
        {
            await _sender.Send(
                new LeaveWaitingListCommand(
                    WaitingListId: id,
                    AuthenticatedUserId: isStaff ? null : currentUserId,
                    Reason: reason),
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

    [Authorize(Policy = "Recepcionista")]
    [HttpGet]
    [ProducesResponseType(typeof(List<WaitingListEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCandidates(
        [FromQuery] string? specialty = null,
        [FromQuery] WaitingListStatus? status = WaitingListStatus.Active,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        [FromQuery] Guid? staffId = null,
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var entries = await _sender.Send(
            new GetWaitingListCandidatesQuery(
                Specialty: specialty,
                Status: status,
                FromDate: fromDate,
                ToDate: toDate,
                StaffId: staffId,
                TenantId: tenantId),
            cancellationToken);

        return Ok(entries);
    }

    [Authorize(Policy = "Paciente")]
    [HttpGet("offers/me")]
    [ProducesResponseType(typeof(List<WaitingListOfferDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyOffers(CancellationToken cancellationToken = default)
    {
        var currentUserId = GetCurrentUserId();
        if (!currentUserId.HasValue)
            return Forbid();

        try
        {
            var offers = await _sender.Send(new GetMyWaitingListOffersQuery(currentUserId.Value), cancellationToken);
            return Ok(offers);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [Authorize(Policy = "Paciente")]
    [HttpPost("offers/{offerId:guid}/accept")]
    [ProducesResponseType(typeof(AcceptWaitingListOfferResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptOffer(
        [FromRoute] Guid offerId,
        [FromBody] AcceptOfferRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var isStaff = IsReceptionOrAdmin();
        var currentUserId = GetCurrentUserId();
        if (!isStaff && !currentUserId.HasValue)
            return Forbid();

        try
        {
            var result = await _sender.Send(
                new AcceptWaitingListOfferCommand(
                    OfferId: offerId,
                    RawToken: null,
                    AuthenticatedUserId: isStaff ? null : currentUserId,
                    ExpectedPatientId: null,
                    SymptomsRaw: request?.SymptomsRaw,
                    Channel: isStaff ? "reception" : "portal"),
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
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [Authorize(Policy = "Paciente")]
    [HttpPost("offers/{offerId:guid}/decline")]
    [ProducesResponseType(typeof(DeclineWaitingListOfferResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeclineOffer(
        [FromRoute] Guid offerId,
        [FromBody] DeclineOfferRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var isStaff = IsReceptionOrAdmin();
        var currentUserId = GetCurrentUserId();
        if (!isStaff && !currentUserId.HasValue)
            return Forbid();

        try
        {
            var result = await _sender.Send(
                new DeclineWaitingListOfferCommand(
                    OfferId: offerId,
                    RawToken: null,
                    AuthenticatedUserId: isStaff ? null : currentUserId,
                    ExpectedPatientId: null,
                    Reason: request?.Reason),
                cancellationToken);

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
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

    private bool IsReceptionOrAdmin()
        => User.IsInRole(nameof(UserRole.Recepcionista)) || User.IsInRole(nameof(UserRole.Administrador));

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) && userId != Guid.Empty ? userId : null;
    }
}
