using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class OverbookCollisionEventRepository : IOverbookCollisionEventRepository
{
    private readonly ApplicationDbContext _dbContext;

    public OverbookCollisionEventRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(OverbookCollisionEvent collisionEvent, CancellationToken cancellationToken = default)
    {
        await _dbContext.OverbookCollisionEvents.AddAsync(collisionEvent, cancellationToken);
    }

    public async Task<bool> ExistsForTriggerAsync(
        Guid originalAppointmentId,
        Guid overbookBlockId,
        string triggerSource,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.OverbookCollisionEvents
            .AsNoTracking()
            .AnyAsync(
                e => e.OriginalAppointmentId == originalAppointmentId
                     && e.OverbookBlockId == overbookBlockId
                     && e.TriggerSource == triggerSource,
                cancellationToken);
    }
}
