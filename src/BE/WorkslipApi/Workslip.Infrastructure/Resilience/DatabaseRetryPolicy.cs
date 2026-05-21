using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Workslip.Infrastructure.Resilience;

public interface IDatabaseRetryPolicy
{
    Task ExecuteAsync(string operationName, Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
    Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}

public sealed class PollyDatabaseRetryPolicy(ILogger<PollyDatabaseRetryPolicy> logger) : IDatabaseRetryPolicy
{
    private const int MaxRetryAttempts = 3;

    public Task ExecuteAsync(string operationName, Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        ExecuteAsync<object?>(operationName, async token =>
        {
            await operation(token);
            return null;
        }, cancellationToken);

    public async Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var pipeline = new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                ShouldHandle = args => new ValueTask<bool>(IsRetryable(args.Outcome.Exception)),
                MaxRetryAttempts = MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    var exception = args.Outcome.Exception;
                    logger.LogWarning(
                        exception,
                        "Database operation retrying. Operation={DatabaseOperation} Attempt={RetryAttempt} DelayMs={RetryDelayMs} ExceptionType={ExceptionType} SqlErrorNumber={SqlErrorNumber}",
                        operationName,
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds,
                        exception?.GetType().Name,
                        GetSqlErrorNumber(exception));

                    return default;
                }
            })
            .Build();

        return await pipeline.ExecuteAsync(async token => await operation(token), cancellationToken);
    }

    private static bool IsRetryable(Exception? exception) => exception switch
    {
        null => false,
        OperationCanceledException => false,
        TimeoutException => true,
        SqlException sqlException => sqlException.Errors.Cast<SqlError>().Any(error => IsTransientSqlError(error.Number)),
        _ => false
    };

    private static int? GetSqlErrorNumber(Exception? exception) =>
        exception is SqlException sqlException && sqlException.Errors.Count > 0
            ? sqlException.Errors[0].Number
            : null;

    private static bool IsTransientSqlError(int number) => number is
        -2 or     // SQL timeout
        64 or     // connection failure
        233 or    // connection initialization failure
        258 or    // connection wait timeout
        1205 or   // deadlock victim
        4060 or   // database unavailable
        10928 or 10929 or // resource throttling
        40143 or 40197 or 40501 or 40613 or // Azure SQL transient/failover/throttling
        49918 or 49919 or 49920 or // Azure SQL processing limits
        10053 or 10054 or 10060 or 11001; // network/socket/DNS transient failures
}
