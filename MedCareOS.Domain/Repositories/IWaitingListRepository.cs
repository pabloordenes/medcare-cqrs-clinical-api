using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Repositories;

public interface IWaitingListRepository
{
    Task<WaitingList?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<WaitingList>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<bool> HasActiveEntryAsync(Guid tenantId, Guid patientId, string specialty, CancellationToken cancellationToken = default);
    Task<List<WaitingList>> QueryEligibleCandidatesAsync(
        Guid tenantId,
        string specialty,
        DateTime slotStartUtc,
        DateTime slotEndUtc,
        TimeZoneInfo tenantTimeZone,
        IReadOnlyCollection<Guid> excludedWaitingListIds,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);
    Task<List<WaitingList>> QueryAsync(
        Guid tenantId,
        string? specialty,
        WaitingListStatus? status,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken = default);
    Task AddAsync(WaitingList entry, CancellationToken cancellationToken = default);
}
