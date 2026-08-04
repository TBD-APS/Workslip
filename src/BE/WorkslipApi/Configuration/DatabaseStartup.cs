using Microsoft.EntityFrameworkCore;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Configuration;

public static class DatabaseStartup
{
    public const string GenerateOpenApiOnlyKey = "Workslip:GenerateOpenApiOnly";

    public static async Task InitializeIfRequiredAsync(
        IServiceProvider services,
        IConfiguration configuration,
        bool releaseTestingEnabled)
    {
        if (configuration.GetValue<bool>(GenerateOpenApiOnlyKey))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        await scope.ServiceProvider
            .GetRequiredService<DatabaseSchemaInitializer>()
            .InitializeAsync();
        await db.Database.CanConnectAsync();

        if (releaseTestingEnabled)
        {
            await scope.ServiceProvider
                .GetRequiredService<DevelopmentDatabaseSeeder>()
                .SeedAsync();
        }
    }
}
