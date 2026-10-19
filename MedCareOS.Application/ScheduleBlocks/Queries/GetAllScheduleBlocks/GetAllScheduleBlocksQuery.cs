using MedCareOS.Domain.Entities;
using MediatR;

namespace MedCareOS.Application.ScheduleBlocks.Queries.GetAllScheduleBlocks;

public record GetAllScheduleBlocksQuery(
    ) : IRequest<List<ScheduleBlock>>;