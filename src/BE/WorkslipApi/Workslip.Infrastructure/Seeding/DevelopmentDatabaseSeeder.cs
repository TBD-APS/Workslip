namespace Workslip.Infrastructure.Schema;

public sealed class DevelopmentDatabaseSeeder(
    SqlDbContext db,
    InstallationBaselineProvisioner installationBaselineProvisioner,
    PlatformIdentityBootstrapper platformIdentityBootstrapper)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await DatabaseSeeder.Seed(db, installationBaselineProvisioner, cancellationToken);
        await platformIdentityBootstrapper.BootstrapAsync(cancellationToken);
    }
}
