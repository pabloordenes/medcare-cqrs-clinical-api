using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class PatientActionTokenRepository : IPatientActionTokenRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PatientActionTokenRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PatientActionToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return await _dbContext.PatientActionTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
    }

    public async Task RevokeActiveForNotificationAsync(
        Guid notificationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var activeTokens = await _dbContext.PatientActionTokens
            .Where(t => t.NotificationId == notificationId
                        && t.UsedAt == null
                        && t.RevokedAt == null
                        && t.ExpiresAt > utcNow)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke(utcNow);
        }
    }

    public async Task AddAsync(PatientActionToken token, CancellationToken cancellationToken = default)
    {
        await _dbContext.PatientActionTokens.AddAsync(token, cancellationToken);
    }

    public async Task<int> DeleteExpiredBeforeAsync(
        DateTimeOffset cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var expired = await _dbContext.PatientActionTokens
            .Where(t => t.ExpiresAt < cutoffUtc)
            .OrderBy(t => t.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
            return 0;

        _dbContext.PatientActionTokens.RemoveRange(expired);
        return expired.Count;
    }
}
