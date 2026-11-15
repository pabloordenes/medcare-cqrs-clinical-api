using MedCareOS.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Resolutions.Queries.GetPendingResolutions;

public class GetPendingResolutionsQueryHandler : IRequestHandler<GetPendingResolutionsQuery, List<ResolutionTaskDto>>
{
    private readonly IApplicationDbContext _dbContext;

    public GetPendingResolutionsQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ResolutionTaskDto>> Handle(GetPendingResolutionsQuery request, CancellationToken cancellationToken)
    {
        var pendingResolutions = await _dbContext.AiResolutionTasks
            .AsNoTracking()
            .Where(t => t.Status == "Pending")
            .OrderByDescending(t => t.RiskProbability) // criticas primero
            .Select(t => new ResolutionTaskDto(
                t.Id,
                t.ResourceId,
                t.RiskProbability,
                t.AiPlanJson,
                t.Status,
                t.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return pendingResolutions;
    }
}