using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Configuration;

public static class DatabaseStartup
{
    public const string GenerateOpenApiOnlyKey = "Workslip:GenerateOpenApiOnly";
    public const string SeedDevelopmentDataKey = "Workslip:SeedDevelopmentData";
    public const string SeedDevelopmentEntraIdentitiesKey = "Workslip:SeedDevelopmentEntraIdentities";
    public const string ApplyLocalMigrationsKey = "Workslip:ApplyLocalMigrations";
    private const string SqlConnectionStringKey = "Azure:Sql:ConnectionString";

    public static bool IsOpenApiGeneration(IConfiguration configuration) =>
        configuration.GetValue<bool>(GenerateOpenApiOnlyKey);

    public static bool ShouldSeedDevelopmentData(
        IHostEnvironment environment,
        IConfiguration configuration) =>
        environment.IsDevelopment()
        && configuration.GetValue<bool>(SeedDevelopmentDataKey);

    public static bool ShouldSeedDevelopmentEntraIdentities(
        IHostEnvironment environment,
        IConfiguration configuration) =>
        ShouldSeedDevelopmentData(environment, configuration)
        && configuration.GetValue<bool>(SeedDevelopmentEntraIdentitiesKey);

    public static bool ShouldApplyLocalMigrations(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        if (!environment.IsDevelopment())
            return false;

        var configured = configuration.GetValue<bool?>(ApplyLocalMigrationsKey);
        if (configured is false)
            return false;

        var connectionString = configuration[SqlConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        var isLocalTarget = LocalDevelopmentDatabaseMigrationRunner.IsLocalSqlTarget(connectionString);
        if (configured is true && !isLocalTarget)
        {
            throw new InvalidOperationException(
                $"{ApplyLocalMigrationsKey}=true requires a provably local SQL target. " +
                "Remote or ambiguous SQL targets are never auto-migrated by Workslip startup.");
        }

        return isLocalTarget;
    }

    public static async Task VerifyIfRequiredAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        IConfiguration configuration,
        bool seedDevelopmentData,
        bool seedDevelopmentEntraIdentities)
    {
        if (IsOpenApiGeneration(configuration))
        {
            Log.Information("[STARTUP 08] Database verification - SKIPPED (OpenAPI generation mode)");
            return;
        }

        if (seedDevelopmentEntraIdentities && !seedDevelopmentData)
        {
            throw new InvalidOperationException(
                $"{SeedDevelopmentEntraIdentitiesKey} requires {SeedDevelopmentDataKey}=true.");
        }

        if (ShouldApplyLocalMigrations(environment, configuration))
        {
            await RunDatabasePhaseAsync(
                "08.1",
                "Apply pending local database migrations",
                () => LocalDevelopmentDatabaseMigrationRunner.ApplyPendingAsync(
                    configuration[SqlConnectionStringKey]!,
                    environment.ContentRootPath));
        }
        else
        {
            Log.Information(
                "[STARTUP 08.1] Apply pending local database migrations - SKIPPED (non-local target, non-Development environment, or explicitly disabled)");
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        await RunDatabasePhaseAsync(
            "08.2",
            "Verify database connectivity",
            async () =>
            {
                if (!await db.Database.CanConnectAsync())
                {
                    throw new InvalidOperationException("Database connectivity check returned false.");
                }
            });

        if (!seedDevelopmentData)
        {
            Log.Information("[STARTUP 08.3] Seed development database - SKIPPED (not explicitly enabled)");
            return;
        }

        if (seedDevelopmentEntraIdentities)
        {
            await RunDatabasePhaseAsync(
                "08.3",
                "Seed development database and reconcile Entra identities",
                () => scope.ServiceProvider
                    .GetRequiredService<DevelopmentDatabaseSeeder>()
                    .SeedAsync());
            return;
        }

        await RunDatabasePhaseAsync(
            "08.3",
            "Seed development database (DB only)",
            () => DatabaseSeeder.Seed(
                db,
                scope.ServiceProvider.GetRequiredService<InstallationBaselineProvisioner>()));
    }

    private static async Task RunDatabasePhaseAsync(string step, string phase, Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        Log.Information("[STARTUP {StartupStep}] {StartupPhase} - START", step, phase);

        try
        {
            await action();
            Log.Information(
                "[STARTUP {StartupStep}] {StartupPhase} - OK ({ElapsedMilliseconds} ms)",
                step,
                phase,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            Log.Warning(
                "[STARTUP {StartupStep}] {StartupPhase} - FAILED after {ElapsedMilliseconds} ms ({ExceptionType})",
                step,
                phase,
                stopwatch.ElapsedMilliseconds,
                exception.GetType().Name);
            throw;
        }
    }
}
