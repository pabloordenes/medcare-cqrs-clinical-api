using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Enums;
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
            .Where(t => t.Status == AiResolutionStatus.Pending &&
                        !_dbContext.Appointments.Any(a =>
                            a.Id == t.ResourceId &&
                            (a.OrchestrationSuppressed ||
                             a.Status != AppointmentStatus.Scheduled ||
                             a.ConfirmationState == ConfirmationState.ConfirmedEarly ||
                             a.ConfirmationState == ConfirmationState.FinalConfirmed ||
                             a.ConfirmationState == ConfirmationState.DeclinedByPatient)))
            .OrderByDescending(t => t.RiskProbability) // criticas primero
            .Select(t => new ResolutionTaskDto(
                t.Id,
                t.ResourceId,
                t.RiskProbability,
                t.AiPlanJson,
                t.Status.ToString(),
                t.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return pendingResolutions;
    }
}