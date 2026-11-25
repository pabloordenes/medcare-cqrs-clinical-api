using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class AppointmentScheduledActionRepository : IAppointmentScheduledActionRepository
{
    private static readonly SemaphoreSlim InMemoryClaimGate = new(1, 1);

    private readonly ApplicationDbContext _dbContext;

    public AppointmentScheduledActionRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddRangeAsync(
        IEnumerable<AppointmentScheduledAction> actions,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.AppointmentScheduledActions.AddRangeAsync(actions, cancellationToken);
    }

    public async Task<AppointmentScheduledAction?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AppointmentScheduledActions
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<List<AppointmentScheduledAction>> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AppointmentScheduledActions
            .AsNoTracking()
            .Where(a => a.AppointmentId == appointmentId)
            .OrderBy(a => a.PlanVersion)
            .ThenBy(a => a.DueAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AppointmentScheduledAction>> GetPendingByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AppointmentScheduledActions
            .Where(a => a.AppointmentId == appointmentId &&
                        (a.Status == ScheduledActionStatus.Pending || a.Status == ScheduledActionStatus.Processing))
            .OrderBy(a => a.DueAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetMaxPlanVersionAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AppointmentScheduledActions
            .AsNoTracking()
            .Where(a => a.AppointmentId == appointmentId)
            .Select(a => (int?)a.PlanVersion)
            .MaxAsync(cancellationToken) ?? 0;
    }

    public async Task<List<AppointmentScheduledAction>> ClaimDueBatchAsync(
        string workerId,
        DateTimeOffset utcNow,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("WorkerId es obligatorio.", nameof(workerId));

        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "LeaseDuration debe ser mayor a cero.");

        if (batchSize <= 0)
            return [];

        var boundedWorkerId = workerId.Trim();
        if (boundedWorkerId.Length > AppointmentScheduledAction.MaxLockedByLength)
            boundedWorkerId = boundedWorkerId[..AppointmentScheduledAction.MaxLockedByLength];

        var leaseUntil = utcNow.Add(leaseDuration);
        var lockToken = Guid.NewGuid();

        if (_dbContext.Database.IsNpgsql())
        {
            const string sql = """
                WITH candidates AS (
                    SELECT id
                    FROM appointment_scheduled_actions
                    WHERE (status = 'Pending' AND due_at <= @utcNow)
                       OR (status = 'Processing' AND lease_until <= @leaseThreshold)
                    ORDER BY due_at ASC
                    LIMIT @batchSize
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE appointment_scheduled_actions a
                SET status      = 'Processing',
                    locked_by   = @workerId,
                    locked_at   = @utcNow,
                    lease_until = @leaseUntil,
                    lock_token  = @lockToken,
                    attempts    = a.attempts + 1,
                    updated_at  = @utcNow
                FROM candidates c
                WHERE a.id = c.id
                RETURNING a.*;
                """;

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var utcNowParam = new NpgsqlParameter("@utcNow", utcNow.UtcDateTime);
            var leaseThresholdParam = new NpgsqlParameter("@leaseThreshold", utcNow.UtcDateTime);
            var batchSizeParam = new NpgsqlParameter("@batchSize", batchSize);
            var workerIdParam = new NpgsqlParameter("@workerId", boundedWorkerId);
            var leaseUntilParam = new NpgsqlParameter("@leaseUntil", leaseUntil.UtcDateTime);
            var lockTokenParam = new NpgsqlParameter("@lockToken", lockToken);

            var claimed = await _dbContext.AppointmentScheduledActions
                .FromSqlRaw(
                    sql,
                    utcNowParam,
                    leaseThresholdParam,
                    batchSizeParam,
                    workerIdParam,
                    leaseUntilParam,
                    lockTokenParam)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            claimed.Sort((a, b) => a.DueAt.CompareTo(b.DueAt));
            return claimed;
        }

        // Rama de fallback para proveedor InMemory en pruebas sin Postgres
        await InMemoryClaimGate.WaitAsync(cancellationToken);
        try
        {
            var candidates = await _dbContext.AppointmentScheduledActions
                .Where(a => (a.Status == ScheduledActionStatus.Pending && a.DueAt <= utcNow) ||
                            (a.Status == ScheduledActionStatus.Processing && a.LeaseUntil <= utcNow))
                .OrderBy(a => a.DueAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
                return [];

            foreach (var action in candidates)
            {
                action.Claim(boundedWorkerId, utcNow, leaseDuration, lockToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            foreach (var action in candidates)
            {
                _dbContext.Entry(action).State = EntityState.Detached;
            }

            return candidates;
        }
        finally
        {
            InMemoryClaimGate.Release();
        }
    }
}
