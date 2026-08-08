namespace Workslip.Infrastructure.Schema;

public sealed class DevelopmentDatabaseSeeder(
    PlatformIdentityBootstrapper platformIdentityBootstrapper,
    SqlDbContext db)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await platformIdentityBootstrapper.BootstrapAsync(cancellationToken);
        await DatabaseSeeder.Seed(db);
    }
}
