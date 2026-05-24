using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Workslip.Application;
using Workslip.Infrastructure.Configuration;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure;

public interface ISqlConnectionFactory
{
    Task<IDbConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}

public sealed class SqlConnectionFactory(
    IConfiguration configuration,
    IDatabaseRetryPolicy retryPolicy,
    ILogger<SqlConnectionFactory> logger,
    ICorrelationIdAccessor correlationIdAccessor) : ISqlConnectionFactory
{
    public Task<IDbConnection> OpenConnectionAsync(CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("sql.open_connection", async token =>
        {
            var connectionString = ResolveConnectionString(configuration);
            var correlationId = correlationIdAccessor.CorrelationId;

            var connection = new SqlConnection(connectionString);

            try
            {
                await connection.OpenAsync(token);
                return (IDbConnection)connection;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SQL connection open failed. CorrelationId={CorrelationId}", correlationId);
                await connection.DisposeAsync();
                throw;
            }
        }, cancellationToken);

    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var connectionString = ConfiguredValues.FirstConfigured(
            configuration.GetConnectionString("JobDB"),
            configuration["Sql:ConnectionString"]);

        return connectionString ?? throw new InvalidOperationException("Missing SQL connection string. Configure ConnectionStrings:JobDB or Sql:ConnectionString.");
    }

}
