using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class OverbookPolicyAuditLogRepository : IOverbookPolicyAuditLogRepository
{
    private readonly ApplicationDbContext _dbContext;

    public OverbookPolicyAuditLogRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(OverbookPolicyAuditLog log, CancellationToken cancellationToken = default)
        => await _dbContext.OverbookPolicyAuditLogs.AddAsync(log, cancellationToken);

    public async Task<List<OverbookPolicyAuditLog>> GetByTenantIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => await _dbContext.OverbookPolicyAuditLogs
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId)
            .OrderByDescending(l => l.ChangedAtUtc)
            .ToListAsync(cancellationToken);
}
