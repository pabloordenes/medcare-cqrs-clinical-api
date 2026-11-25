using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.WaitingLists.Commands.AcceptWaitingListOffer;

public sealed record AcceptWaitingListOfferResult(
    bool IsSuccess,
    Guid? OfferId,
    Guid? AppointmentId,
    Guid? PatientId,
    WaitingListOfferStatus? OfferStatus,
    bool WasIdempotentReplay = false,
    string? Message = null);

public sealed record AcceptWaitingListOfferCommand(
    Guid? OfferId,
    string? RawToken,
    Guid? AuthenticatedUserId,
    Guid? ExpectedPatientId,
    string? SymptomsRaw = null,
    string Channel = "portal") : IRequest<AcceptWaitingListOfferResult>;
