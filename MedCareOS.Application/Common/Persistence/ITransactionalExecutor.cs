namespace MedCareOS.Application.Common.Persistence;

public interface ITransactionalExecutor
{
    Task ExecuteAsync(
        string lockKey,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    Task<TResult> ExecuteAsync<TResult>(
        string lockKey,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    Task ExecuteOrderedAsync(
        IEnumerable<string> lockKeys,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    Task<TResult> ExecuteOrderedAsync<TResult>(
        IEnumerable<string> lockKeys,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
