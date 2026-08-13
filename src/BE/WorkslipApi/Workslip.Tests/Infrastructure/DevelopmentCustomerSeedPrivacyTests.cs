using Microsoft.EntityFrameworkCore;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class DevelopmentCustomerSeedPrivacyTests
{
    [Fact]
    public async Task Seed_uses_only_explicitly_synthetic_demo_customers()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new SqlDbContext(options);

        await DatabaseSeeder.Seed(
            context,
            new InstallationBaselineProvisioner(context));

        var customers = await context.Customers
            .AsNoTracking()
            .OrderBy(customer => customer.CustomerNumber)
            .ToListAsync();

        Assert.Equal(40, customers.Count);
        Assert.All(customers, customer =>
        {
            Assert.True(customer.Name.StartsWith("Demokunde ", StringComparison.Ordinal));
            Assert.True(customer.CustomerNumber?.StartsWith("DEMO-", StringComparison.Ordinal) == true);
            Assert.True(customer.Email?.EndsWith("@example.invalid", StringComparison.OrdinalIgnoreCase) == true);
            Assert.True(customer.Address?.StartsWith("Testvej ", StringComparison.Ordinal) == true);
        });
    }
}
