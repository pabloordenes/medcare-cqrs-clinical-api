using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.WaitingLists.Queries.GetMyWaitingListOffers;

public sealed record WaitingListOfferDto(
    Guid Id,
    Guid WaitingListId,
    Guid ScheduleBlockId,
    string SourceType,
    int CascadeAttemptNumber,
    WaitingListOfferStatus Status,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    string Specialty,
    string DoctorName,
    DateTimeOffset OfferedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt);

public sealed record GetMyWaitingListOffersQuery(Guid AuthenticatedUserId) : IRequest<List<WaitingListOfferDto>>;
