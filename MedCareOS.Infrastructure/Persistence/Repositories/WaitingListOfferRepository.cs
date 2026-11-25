using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class WaitingListOfferRepository : IWaitingListOfferRepository
{
    private readonly ApplicationDbContext _dbContext;

    public WaitingListOfferRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WaitingListOffer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _dbContext.WaitingListOffers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<WaitingListOffer?> GetPendingByScheduleBlockIdAsync(
        Guid scheduleBlockId,
        CancellationToken cancellationToken = default)
        => await _dbContext.WaitingListOffers.FirstOrDefaultAsync(
            x => x.ScheduleBlockId == scheduleBlockId && x.Status == WaitingListOfferStatus.Pending,
            cancellationToken);

    public async Task<List<WaitingListOffer>> GetPendingByPatientIdAsync(
        Guid patientId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
        => await _dbContext.WaitingListOffers
            .AsNoTracking()
            .Where(x => x.PatientId == patientId
                        && x.Status == WaitingListOfferStatus.Pending
                        && x.ExpiresAt > utcNow)
            .OrderByDescending(x => x.OfferedAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> HasActivePendingOfferForBlockAsync(
        Guid scheduleBlockId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
        => await _dbContext.WaitingListOffers
            .AsNoTracking()
            .AnyAsync(
                x => x.ScheduleBlockId == scheduleBlockId
                     && x.Status == WaitingListOfferStatus.Pending
                     && x.ExpiresAt > utcNow,
                cancellationToken);

    public async Task<HashSet<Guid>> GetActivePendingBlockIdsAsync(
        IReadOnlyCollection<Guid> scheduleBlockIds,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        if (scheduleBlockIds.Count == 0)
            return [];

        var ids = await _dbContext.WaitingListOffers
            .AsNoTracking()
            .Where(x => scheduleBlockIds.Contains(x.ScheduleBlockId)
                        && x.Status == WaitingListOfferStatus.Pending
                        && x.ExpiresAt > utcNow)
            .Select(x => x.ScheduleBlockId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    public async Task<List<Guid>> GetAttemptedWaitingListIdsForBlockAsync(
        Guid scheduleBlockId,
        CancellationToken cancellationToken = default)
        => await _dbContext.WaitingListOffers
            .AsNoTracking()
            .Where(x => x.ScheduleBlockId == scheduleBlockId)
            .Select(x => x.WaitingListId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<int> CountOffersForBlockAsync(
        Guid scheduleBlockId,
        CancellationToken cancellationToken = default)
        => await _dbContext.WaitingListOffers
            .AsNoTracking()
            .CountAsync(x => x.ScheduleBlockId == scheduleBlockId, cancellationToken);

    public async Task<List<WaitingListOffer>> ClaimExpiredPendingOffersAsync(
        DateTimeOffset utcNow,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.IsNpgsql())
        {
            return await _dbContext.WaitingListOffers
                .FromSqlInterpolated($"""
                    SELECT * FROM waiting_list_offers
                    WHERE status = 'Pending' AND expires_at <= {utcNow}
                    ORDER BY expires_at
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        return await _dbContext.WaitingListOffers
            .AsNoTracking()
            .Where(x => x.Status == WaitingListOfferStatus.Pending && x.ExpiresAt <= utcNow)
            .OrderBy(x => x.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(WaitingListOffer offer, CancellationToken cancellationToken = default)
        => await _dbContext.WaitingListOffers.AddAsync(offer, cancellationToken);
}
