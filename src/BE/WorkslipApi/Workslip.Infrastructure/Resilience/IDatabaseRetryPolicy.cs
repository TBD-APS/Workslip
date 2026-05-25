namespace Workslip.Infrastructure.Resilience;

public interface IDatabaseRetryPolicy
{
    Task ExecuteAsync(string operationName, Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}