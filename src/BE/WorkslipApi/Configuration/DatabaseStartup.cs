using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Configuration;

public static class DatabaseStartup
{
    public const string GenerateOpenApiOnlyKey = "Workslip:GenerateOpenApiOnly";
    public const string SeedDevelopmentDataKey = "Workslip:SeedDevelopmentData";

    public static bool IsOpenApiGeneration(IConfiguration configuration) =>
        configuration.GetValue<bool>(GenerateOpenApiOnlyKey);

    public static bool ShouldSeedDevelopmentData(
        IHostEnvironment environment,
        IConfiguration configuration) =>
        environment.IsDevelopment()
        && configuration.GetValue<bool>(SeedDevelopmentDataKey);

    public static async Task VerifyIfRequiredAsync(
        IServiceProvider services,
        IConfiguration configuration,
        bool seedDevelopmentData)
    {
        if (IsOpenApiGeneration(configuration))
        {
            Log.Information("[STARTUP 08] Database verification - SKIPPED (OpenAPI generation mode)");
            return;
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

        if (seedDevelopmentData)
        {
            await RunDatabasePhaseAsync(
                "08.2",
                "Seed development database",
                () => scope.ServiceProvider
                    .GetRequiredService<DevelopmentDatabaseSeeder>()
                    .SeedAsync());
        }
        else
        {
            Log.Information("[STARTUP 08.2] Seed development database - SKIPPED (not explicitly enabled)");
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
                "[STARTUP {StartupStep}] {StartupPhase} - FAILED after {ElapsedMilliseconds} ms ({ExceptionType})",
                step,
                phase,
                stopwatch.ElapsedMilliseconds,
                exception.GetType().Name);
            throw;
        }
    }
}
