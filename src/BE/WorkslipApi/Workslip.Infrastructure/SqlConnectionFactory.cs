using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure;

public interface ISqlConnectionFactory
{
    Task<IDbConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}

public sealed class SqlConnectionFactory(IConfiguration configuration, IDatabaseRetryPolicy retryPolicy) : ISqlConnectionFactory
{
    public Task<IDbConnection> OpenConnectionAsync(CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("sql.open_connection", async token =>
        {
            var connectionString = ResolveConnectionString(configuration);
            var connection = new SqlConnection(connectionString);

            try
            {
                await connection.OpenAsync(token);
                return (IDbConnection)connection;
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }, cancellationToken);

    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var connectionString = FirstConfigured(
            configuration.GetConnectionString("JobDB"),
            configuration["Sql:ConnectionString"]);

        return connectionString
            ?? throw new InvalidOperationException(
                "Missing SQL connection string. Configure ConnectionStrings:JobDB or Sql:ConnectionString.");
    }

    private static string? FirstConfigured(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
