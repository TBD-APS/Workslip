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

    public static async Task VerifyIfRequiredAsync(
        IServiceProvider services,
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

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        await RunDatabasePhaseAsync(
            "08.1",
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
            Log.Information("[STARTUP 08.2] Seed development database - SKIPPED (not explicitly enabled)");
            return;
        }

        if (seedDevelopmentEntraIdentities)
        {
            await RunDatabasePhaseAsync(
                "08.2",
                "Seed development database and reconcile Entra identities",
                () => scope.ServiceProvider
                    .GetRequiredService<DevelopmentDatabaseSeeder>()
                    .SeedAsync());
            return;
        }

        await RunDatabasePhaseAsync(
            "08.2",
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
