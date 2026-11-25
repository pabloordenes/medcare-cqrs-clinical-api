using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class OverbookShadowDecisionRepository : IOverbookShadowDecisionRepository
{
    private readonly ApplicationDbContext _dbContext;

    public OverbookShadowDecisionRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(OverbookShadowDecision decision, CancellationToken cancellationToken = default)
        => await _dbContext.OverbookShadowDecisions.AddAsync(decision, cancellationToken);

    public async Task AddRangeAsync(IEnumerable<OverbookShadowDecision> decisions, CancellationToken cancellationToken = default)
        => await _dbContext.OverbookShadowDecisions.AddRangeAsync(decisions, cancellationToken);

    public async Task<bool> ExistsForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default)
        => await _dbContext.OverbookShadowDecisions
            .AsNoTracking()
            .AnyAsync(d => d.AppointmentId == appointmentId, cancellationToken);

    public async Task<List<OverbookShadowDecision>> GetUnresolvedByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
        => await _dbContext.OverbookShadowDecisions
            .Where(d => d.AppointmentId == appointmentId && d.OutcomeAttendance == null)
            .ToListAsync(cancellationToken);

    public async Task<List<OverbookShadowDecision>> GetByTenantAsync(
        Guid tenantId,
        bool? isSyntheticOnly = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.OverbookShadowDecisions
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId);

        if (isSyntheticOnly.HasValue)
        {
            query = query.Where(d => d.IsSynthetic == isSyntheticOnly.Value);
        }

        return await query
            .OrderByDescending(d => d.EvaluatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasSyntheticSeedAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await _dbContext.OverbookShadowDecisions
            .AsNoTracking()
            .AnyAsync(d => d.TenantId == tenantId && d.IsSynthetic, cancellationToken);
}
