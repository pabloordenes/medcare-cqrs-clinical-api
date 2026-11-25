using MediatR;

namespace MedCareOS.Application.WaitingLists.Commands.JoinWaitingList;

public sealed record JoinWaitingListCommand(
    Guid? PatientId,
    Guid? AuthenticatedUserId,
    string Specialty,
    int Priority = 0,
    string? ClinicalContextJson = null,
    Guid? PreferredStaffId = null,
    DateOnly? PreferredFromDate = null,
    DateOnly? PreferredToDate = null,
    TimeOnly? PreferredTimeStart = null,
    TimeOnly? PreferredTimeEnd = null,
    Guid? DisplacedSourceAppointmentId = null,
    DateTimeOffset? ExpiresAt = null,
    Guid? TenantId = null) : IRequest<Guid>;
