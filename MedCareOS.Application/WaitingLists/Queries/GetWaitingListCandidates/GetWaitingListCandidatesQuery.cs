using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.WaitingLists.Queries.GetWaitingListCandidates;

public sealed record GetWaitingListCandidatesQuery(
    string? Specialty = null,
    WaitingListStatus? Status = WaitingListStatus.Active,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    Guid? StaffId = null,
    Guid? TenantId = null) : IRequest<List<WaitingListEntryDto>>;
