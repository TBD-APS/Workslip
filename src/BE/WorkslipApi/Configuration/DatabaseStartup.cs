using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Configuration;

public static class DatabaseStartup
{
    public const string GenerateOpenApiOnlyKey = "Workslip:GenerateOpenApiOnly";

    public static bool IsOpenApiGeneration(IConfiguration configuration) =>
        configuration.GetValue<bool>(GenerateOpenApiOnlyKey);

    public static async Task InitializeIfRequiredAsync(
        IServiceProvider services,
        IConfiguration configuration,
        bool releaseTestingEnabled)
    {
        if (IsOpenApiGeneration(configuration))
        {
            Log.Information("[STARTUP 08] Database initialization - SKIPPED (OpenAPI generation mode)");
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        await RunDatabasePhaseAsync(
            "08.1",
            "Initialize database schema and migrations",
            () => scope.ServiceProvider
                .GetRequiredService<DatabaseSchemaInitializer>()
                .InitializeAsync());

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

        if (releaseTestingEnabled)
        {
            await RunDatabasePhaseAsync(
                "08.3",
                "Seed release-test database",
                () => scope.ServiceProvider
                    .GetRequiredService<DevelopmentDatabaseSeeder>()
                    .SeedAsync());
        }
        else
        {
            Log.Information("[STARTUP 08.3] Seed release-test database - SKIPPED (release testing disabled)");
        }
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
            Log.Error(
                exception,
                "[STARTUP {StartupStep}] {StartupPhase} - FAILED after {ElapsedMilliseconds} ms",
                step,
                phase,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
