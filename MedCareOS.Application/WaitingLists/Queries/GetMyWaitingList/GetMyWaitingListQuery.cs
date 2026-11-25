using MediatR;

namespace MedCareOS.Application.WaitingLists.Queries.GetMyWaitingList;

public sealed record GetMyWaitingListQuery(Guid AuthenticatedUserId) : IRequest<List<WaitingListEntryDto>>;
