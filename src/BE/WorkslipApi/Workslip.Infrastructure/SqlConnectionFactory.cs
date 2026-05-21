using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Workslip.Data;

public interface ISqlConnectionFactory
{
    Task<IDbConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}

public sealed class SqlConnectionFactory(IConfiguration configuration) : ISqlConnectionFactory
{
    public async Task<IDbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = ResolveConnectionString(configuration);

        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

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
