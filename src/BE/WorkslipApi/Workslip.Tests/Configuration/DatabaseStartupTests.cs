using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Api.Configuration;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class DatabaseStartupTests
{
    [Fact]
    public async Task InitializeIfRequiredAsync_DuringOpenApiGeneration_DoesNotResolveDatabaseServices()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseStartup.GenerateOpenApiOnlyKey] = "true"
            })
            .Build();

        await DatabaseStartup.InitializeIfRequiredAsync(
            services,
            configuration,
            releaseTestingEnabled: false);
    }

    [Fact]
    public async Task InitializeIfRequiredAsync_DuringNormalRuntime_StillRequiresDatabaseServices()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder().Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseStartup.InitializeIfRequiredAsync(
                services,
                configuration,
                releaseTestingEnabled: false));

        Assert.Contains("SqlDbContext", exception.Message, StringComparison.Ordinal);
    }
}
