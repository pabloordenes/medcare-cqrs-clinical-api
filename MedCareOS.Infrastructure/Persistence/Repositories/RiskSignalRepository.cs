using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class RiskSignalRepository : IRiskSignalRepository
{
    private readonly ApplicationDbContext _dbContext;

    public RiskSignalRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistsByIdempotencyKeyAsync(
        Guid appointmentId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return false;

        var normalizedKey = idempotencyKey.Trim();

        if (_dbContext.RiskSignals.Local.Any(s =>
                s.AppointmentId == appointmentId &&
                string.Equals(s.IdempotencyKey, normalizedKey, StringComparison.Ordinal)))
        {
            return true;
        }

        return await _dbContext.RiskSignals
            .AsNoTracking()
            .AnyAsync(
                s => s.AppointmentId == appointmentId && s.IdempotencyKey == normalizedKey,
                cancellationToken);
    }

    public async Task<List<RiskSignal>> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        var persisted = await _dbContext.RiskSignals
            .AsNoTracking()
            .Where(s => s.AppointmentId == appointmentId)
            .OrderBy(s => s.OccurredAt)
            .ToListAsync(cancellationToken);

        var localAdded = _dbContext.RiskSignals.Local
            .Where(s => s.AppointmentId == appointmentId)
            .ToList();

        if (localAdded.Count == 0)
            return persisted;

        var combined = new List<RiskSignal>(persisted.Count + localAdded.Count);
        var seenIds = new HashSet<Guid>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var signal in persisted)
        {
            if (seenIds.Add(signal.Id) && seenKeys.Add(signal.IdempotencyKey))
            {
                combined.Add(signal);
            }
        }

        foreach (var signal in localAdded)
        {
            if (seenIds.Add(signal.Id) && seenKeys.Add(signal.IdempotencyKey))
            {
                combined.Add(signal);
            }
        }

        combined.Sort((a, b) => a.OccurredAt.CompareTo(b.OccurredAt));
        return combined;
    }

    public async Task AddAsync(RiskSignal signal, CancellationToken cancellationToken = default)
    {
        await _dbContext.RiskSignals.AddAsync(signal, cancellationToken);
    }
}
