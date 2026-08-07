using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Workslip.Api.Configuration;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class DatabaseStartupTests
{
    [Fact]
    public async Task InitializeIfRequiredAsync_DuringOpenApiGeneration_DoesNotResolveDatabaseServices()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = BuildConfiguration(generateOpenApiOnly: true);

        await DatabaseStartup.InitializeIfRequiredAsync(
            services,
            configuration,
            releaseTestingEnabled: false);
    }

    [Fact]
    public async Task InitializeIfRequiredAsync_DuringNormalRuntime_StillRequiresDatabaseServices()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = BuildConfiguration(generateOpenApiOnly: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseStartup.InitializeIfRequiredAsync(
                services,
                configuration,
                releaseTestingEnabled: false));

        Assert.Contains("SqlDbContext", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 3)]
    public void ConfigureServices_RegistersHostedServicesOnlyForRuntime(
        bool generateOpenApiOnly,
        int expectedHostedServiceCount)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.Configuration.AddConfiguration(BuildConfiguration(generateOpenApiOnly));

        builder.ConfigureServices();

        var hostedServiceCount = builder.Services.Count(descriptor =>
            descriptor.ServiceType == typeof(IHostedService));
        Assert.Equal(expectedHostedServiceCount, hostedServiceCount);
    }

    private static IConfiguration BuildConfiguration(bool generateOpenApiOnly) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseStartup.GenerateOpenApiOnlyKey] = generateOpenApiOnly.ToString()
            })
            .Build();
}
