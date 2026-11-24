using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class TenantConfirmationPolicyRepository : ITenantConfirmationPolicyRepository
{
    private readonly ApplicationDbContext _dbContext;

    public TenantConfirmationPolicyRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantConfirmationPolicy?> GetByTenantIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantConfirmationPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken);
    }

    public async Task<TenantConfirmationPolicy> GetOrDefaultAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var policy = await GetByTenantIdAsync(tenantId, cancellationToken);
        return policy ?? TenantConfirmationPolicy.CreateDefault(tenantId);
    }

    public async Task UpsertAsync(
        TenantConfirmationPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var existing = await _dbContext.TenantConfirmationPolicies
            .FirstOrDefaultAsync(p => p.TenantId == policy.TenantId, cancellationToken);

        if (existing is null)
        {
            await _dbContext.TenantConfirmationPolicies.AddAsync(policy, cancellationToken);
            return;
        }

        if (!ReferenceEquals(existing, policy))
        {
            existing.Update(
                policy.Reminder1OffsetHours,
                policy.Reminder2OffsetHours,
                policy.FinalConfirmationOffsetHours,
                policy.DeadlineOffsetHours,
                policy.MinimumDelayAfterCreationMinutes,
                policy.SameDayReminderMinLeadHours,
                policy.TimeZoneId,
                policy.QuietHoursStartLocal,
                policy.QuietHoursEndLocal,
                policy.PrimaryChannelStrategy,
                policy.AlternateChannelStrategy,
                policy.EnableRescoreAtReminder2,
                policy.CountSentAsDeliveredForUnanswered,
                policy.WeightConfirmedEarly,
                policy.WeightReminderUnanswered,
                policy.MaxReminderUnansweredCumulative,
                policy.WeightDeliveryFailed,
                policy.WeightRepliedWithoutConfirmation,
                policy.WeightFinalConfirmed,
                policy.MediumExitThreshold,
                policy.MediumEnterThreshold,
                policy.HighExitThreshold,
                policy.HighEnterThreshold,
                policy.UpdatedAt);
        }
    }
}
