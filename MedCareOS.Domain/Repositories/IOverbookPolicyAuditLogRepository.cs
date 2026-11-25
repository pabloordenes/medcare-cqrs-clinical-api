using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IOverbookPolicyAuditLogRepository
{
    Task AddAsync(OverbookPolicyAuditLog log, CancellationToken cancellationToken = default);
    Task<List<OverbookPolicyAuditLog>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
