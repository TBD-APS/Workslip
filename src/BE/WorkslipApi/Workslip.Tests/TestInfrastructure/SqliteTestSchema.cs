using Microsoft.EntityFrameworkCore;
using Workslip.Infrastructure.Schema;

namespace Workslip.Tests.TestInfrastructure;

internal static class SqliteTestSchema
{
    internal static async Task CreateAsync(
        SqlDbContext context,
        CancellationToken cancellationToken = default)
    {
        var createScript = context.Database.GenerateCreateScript()
            .Replace("nvarchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase);

        await context.Database.ExecuteSqlRawAsync(createScript, cancellationToken);
    }
}