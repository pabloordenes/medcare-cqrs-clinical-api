using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.WaitingLists.Commands.DeclineWaitingListOffer;

public sealed record DeclineWaitingListOfferResult(
    bool IsSuccess,
    Guid OfferId,
    WaitingListOfferStatus OfferStatus,
    bool WasIdempotentReplay = false,
    string? CascadeOutcome = null,
    Guid? NextOfferId = null);

public sealed record DeclineWaitingListOfferCommand(
    Guid? OfferId,
    string? RawToken,
    Guid? AuthenticatedUserId,
    Guid? ExpectedPatientId,
    string? Reason = null) : IRequest<DeclineWaitingListOfferResult>;
