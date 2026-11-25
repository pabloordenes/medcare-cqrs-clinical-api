using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface ITenantConfirmationPolicyRepository
{
    Task<TenantConfirmationPolicy?> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantConfirmationPolicy> GetOrDefaultAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task UpsertAsync(TenantConfirmationPolicy policy, CancellationToken cancellationToken = default);
}
