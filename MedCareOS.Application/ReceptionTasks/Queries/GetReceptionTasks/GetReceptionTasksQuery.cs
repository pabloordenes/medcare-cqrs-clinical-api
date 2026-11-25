using MedCareOS.Domain.Enums;
using MediatR;

namespace MedCareOS.Application.ReceptionTasks.Queries.GetReceptionTasks;

public record GetReceptionTasksQuery(
    ReceptionTaskStatus? Status = null,
    ReceptionTaskPriority? Priority = null,
    Guid? AssignedToUserId = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedReceptionTasksDto>;
