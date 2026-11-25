using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IOverbookShadowDecisionRepository
{
    Task AddAsync(OverbookShadowDecision decision, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<OverbookShadowDecision> decisions, CancellationToken cancellationToken = default);
    Task<bool> ExistsForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default);
    Task<List<OverbookShadowDecision>> GetUnresolvedByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken = default);
    Task<List<OverbookShadowDecision>> GetByTenantAsync(Guid tenantId, bool? isSyntheticOnly = null, CancellationToken cancellationToken = default);
    Task<bool> HasSyntheticSeedAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
