using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IOverbookCollisionEventRepository
{
    Task AddAsync(OverbookCollisionEvent collisionEvent, CancellationToken cancellationToken = default);
    Task<bool> ExistsForTriggerAsync(Guid originalAppointmentId, Guid overbookBlockId, string triggerSource, CancellationToken cancellationToken = default);
}
