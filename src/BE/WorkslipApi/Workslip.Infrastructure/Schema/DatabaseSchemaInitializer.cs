using Microsoft.EntityFrameworkCore;

namespace Workslip.Infrastructure.Schema;

public sealed class DatabaseSchemaInitializer(SqlDbContext db)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("", cancellationToken);
    }
}
