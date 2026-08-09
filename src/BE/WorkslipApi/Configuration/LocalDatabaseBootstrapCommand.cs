using Microsoft.EntityFrameworkCore;
using Workslip.Infrastructure;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Configuration;

public static class LocalDatabaseBootstrapCommand
{
    public const string OperationName = "bootstrap-local-db";

    public static bool IsRequested(IReadOnlyList<string> args)
    {
        var operation = WorkslipOperationParser.Parse(args);
        return string.Equals(operation, OperationName, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task ExecuteAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"'{OperationName}' is Development-only and cannot run in '{environment.EnvironmentName}'.");
        }

        var connectionString = SqlConnectionFactory.ResolveConnectionString(configuration);
        if (!LocalDevelopmentDatabaseMigrationRunner.IsLocalSqlTarget(connectionString))
        {
            throw new InvalidOperationException(
                $"'{OperationName}' requires a provably local SQL Server target. Remote or ambiguous targets are refused.");
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        var created = await db.Database.EnsureCreatedAsync(cancellationToken);
        if (created)
        {
            await LocalDevelopmentDatabaseMigrationRunner.BaselineCurrentSchemaAsync(
                connectionString,
                environment.ContentRootPath,
                cancellationToken);
        }
        else
        {
            await LocalDevelopmentDatabaseMigrationRunner.ApplyPendingAsync(
                connectionString,
                environment.ContentRootPath,
                cancellationToken);
        }

        await DatabaseSeeder.Seed(
            db,
            scope.ServiceProvider.GetRequiredService<InstallationBaselineProvisioner>(),
            cancellationToken);
    }
}
