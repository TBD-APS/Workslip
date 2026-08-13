using Microsoft.EntityFrameworkCore;
using Serilog;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Configuration;

internal static class LocalDevelopmentDatabasePreparation
{
    private const string SqlConnectionStringKey = "Azure:Sql:ConnectionString";

    public static async Task PrepareAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
            return;

        var connectionString = configuration[SqlConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString) ||
            !LocalDevelopmentDatabaseMigrationRunner.IsLocalSqlTarget(connectionString))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        var freshSchema = await LocalDevelopmentDatabaseBootstrapper.EnsureFreshSchemaAsync(
            db,
            connectionString,
            environment.ContentRootPath,
            cancellationToken);

        var databaseIsEmpty = freshSchema ||
            !await db.Organizations.AsNoTracking().AnyAsync(cancellationToken);
        if (!databaseIsEmpty)
            return;

        Log.Information(
            "[STARTUP 08.0] Fresh/empty local database detected; seeding synthetic DB-only development data before migration processing");

        await DatabaseSeeder.Seed(
            db,
            scope.ServiceProvider.GetRequiredService<InstallationBaselineProvisioner>(),
            cancellationToken);
    }
}
