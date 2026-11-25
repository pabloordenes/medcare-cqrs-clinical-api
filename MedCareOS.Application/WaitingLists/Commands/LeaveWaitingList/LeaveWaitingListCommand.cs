using MediatR;

namespace MedCareOS.Application.WaitingLists.Commands.LeaveWaitingList;

public sealed record LeaveWaitingListCommand(
    Guid WaitingListId,
    Guid? AuthenticatedUserId,
    string? Reason = null) : IRequest;
