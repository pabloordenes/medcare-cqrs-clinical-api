using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class WaitingListRepository : IWaitingListRepository
{
    private readonly ApplicationDbContext _context;

    public WaitingListRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<WaitingList?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.WaitingLists.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<List<WaitingList>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
        => await _context.WaitingLists
            .AsNoTracking()
            .Where(x => x.PatientId == patientId)
            .OrderByDescending(x => x.RegistrationDate)
            .ToListAsync(cancellationToken);

    public async Task<bool> HasActiveEntryAsync(
        Guid tenantId,
        Guid patientId,
        string specialty,
        CancellationToken cancellationToken = default)
    {
        var normalized = WaitingList.NormalizeSpecialty(specialty);
        return await _context.WaitingLists.AnyAsync(
            x => x.TenantId == tenantId
                 && x.PatientId == patientId
                 && x.SpecialtyNormalized == normalized
                 && (x.Status == WaitingListStatus.Active || x.Status == WaitingListStatus.Offered),
            cancellationToken);
    }

    public async Task<List<WaitingList>> QueryEligibleCandidatesAsync(
        Guid tenantId,
        string specialty,
        DateTime slotStartUtc,
        DateTime slotEndUtc,
        TimeZoneInfo tenantTimeZone,
        IReadOnlyCollection<Guid> excludedWaitingListIds,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var normalized = WaitingList.NormalizeSpecialty(specialty);
        var slotUtc = DateTime.SpecifyKind(slotStartUtc, DateTimeKind.Utc);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(slotUtc, tenantTimeZone);
        var slotLocalDate = DateOnly.FromDateTime(localStart);
        var slotLocalTime = TimeOnly.FromDateTime(localStart);

        var candidates = await _context.WaitingLists
            .Where(x => x.TenantId == tenantId
                        && x.SpecialtyNormalized == normalized
                        && x.Status == WaitingListStatus.Active
                        && (x.ExpiresAt == null || x.ExpiresAt > utcNow)
                        && (x.PreferredFromDate == null || x.PreferredFromDate <= slotLocalDate)
                        && (x.PreferredToDate == null || x.PreferredToDate >= slotLocalDate))
            .OrderByDescending(x => x.IsDisplacedBonus)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.RegistrationDate)
            .ToListAsync(cancellationToken);

        var excludedSet = excludedWaitingListIds.Count > 0
            ? new HashSet<Guid>(excludedWaitingListIds)
            : null;

        return candidates
            .Where(c => excludedSet == null || !excludedSet.Contains(c.Id))
            .Where(c => !c.PreferredTimeStart.HasValue || !c.PreferredTimeEnd.HasValue
                        || (slotLocalTime >= c.PreferredTimeStart.Value && slotLocalTime < c.PreferredTimeEnd.Value))
            .ToList();
    }

    public async Task<List<WaitingList>> QueryAsync(
        Guid tenantId,
        string? specialty,
        WaitingListStatus? status,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken = default)
    {
        var query = _context.WaitingLists
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(specialty))
        {
            var normalized = WaitingList.NormalizeSpecialty(specialty);
            query = query.Where(x => x.SpecialtyNormalized == normalized);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(x => x.PreferredToDate == null || x.PreferredToDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(x => x.PreferredFromDate == null || x.PreferredFromDate <= toDate.Value);
        }

        return await query
            .OrderByDescending(x => x.IsDisplacedBonus)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.RegistrationDate)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(WaitingList entry, CancellationToken cancellationToken = default)
        => await _context.WaitingLists.AddAsync(entry, cancellationToken);
}
