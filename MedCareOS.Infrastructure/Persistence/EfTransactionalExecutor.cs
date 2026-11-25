using System.Collections.Concurrent;
using MedCareOS.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence;

public class EfTransactionalExecutor : ITransactionalExecutor
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InMemoryGates = new(StringComparer.Ordinal);
    private static readonly AsyncLocal<HashSet<string>?> HeldInMemoryKeys = new();

    private readonly ApplicationDbContext _dbContext;

    public EfTransactionalExecutor(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task ExecuteAsync(
        string lockKey,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
        => ExecuteOrderedAsync([lockKey], operation, cancellationToken);

    public Task<TResult> ExecuteAsync<TResult>(
        string lockKey,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        => ExecuteOrderedAsync([lockKey], operation, cancellationToken);

    public async Task ExecuteOrderedAsync(
        IEnumerable<string> lockKeys,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        await ExecuteOrderedAsync<int>(
            lockKeys,
            async ct =>
            {
                await operation(ct);
                return 0;
            },
            cancellationToken);
    }

    public async Task<TResult> ExecuteOrderedAsync<TResult>(
        IEnumerable<string> lockKeys,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lockKeys);
        ArgumentNullException.ThrowIfNull(operation);

        if (_dbContext.Database.IsNpgsql())
        {
            if (_dbContext.Database.CurrentTransaction is not null)
            {
                return await operation(cancellationToken);
            }

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        // Fallback en memoria: serializa las secciones críticas por clave ordenada para evitar deadlocks y carreras
        var currentHeld = HeldInMemoryKeys.Value ?? [];
        var orderedKeysToAcquire = lockKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.Ordinal)
            .Where(k => !currentHeld.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        if (orderedKeysToAcquire.Count == 0)
        {
            return await operation(cancellationToken);
        }

        var acquired = new List<(string Key, SemaphoreSlim Gate)>(orderedKeysToAcquire.Count);
        var updatedHeld = new HashSet<string>(currentHeld, StringComparer.Ordinal);
        var previousHeld = HeldInMemoryKeys.Value;

        try
        {
            foreach (var key in orderedKeysToAcquire)
            {
                var gate = InMemoryGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(cancellationToken);
                acquired.Add((key, gate));
                updatedHeld.Add(key);
            }

            HeldInMemoryKeys.Value = updatedHeld;
            return await operation(cancellationToken);
        }
        finally
        {
            HeldInMemoryKeys.Value = previousHeld;
            for (int i = acquired.Count - 1; i >= 0; i--)
            {
                acquired[i].Gate.Release();
            }
        }
    }
}
