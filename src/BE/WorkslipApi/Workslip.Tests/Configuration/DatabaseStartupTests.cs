using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Workslip.Api.Configuration;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class DatabaseStartupTests
{
    [Fact]
    public async Task VerifyIfRequiredAsync_DuringOpenApiGeneration_DoesNotResolveDatabaseServices()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = BuildConfiguration(generateOpenApiOnly: true);

        await DatabaseStartup.VerifyIfRequiredAsync(
            services,
            configuration,
            seedDevelopmentData: false);
    }

    [Fact]
    public async Task VerifyIfRequiredAsync_DuringNormalRuntime_VerifiesConnectivityWithoutMigrationServices()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDbContext<SqlDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        await using var services = serviceCollection.BuildServiceProvider();
        var configuration = BuildConfiguration(generateOpenApiOnly: false);

        await DatabaseStartup.VerifyIfRequiredAsync(
            services,
            configuration,
            seedDevelopmentData: false);
    }

    [Fact]
    public async Task VerifyIfRequiredAsync_DuringNormalRuntime_StillRequiresDatabaseServices()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var configuration = BuildConfiguration(generateOpenApiOnly: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseStartup.VerifyIfRequiredAsync(
                services,
                configuration,
                seedDevelopmentData: false));

        Assert.Contains("SqlDbContext", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public void ShouldSeedDevelopmentData_OnlyAllowsAspNetDevelopment(
        string environmentName,
        bool expected)
    {
        var environment = new TestHostEnvironment
        {
            EnvironmentName = environmentName
        };

        Assert.Equal(expected, DatabaseStartup.ShouldSeedDevelopmentData(environment));
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

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Workslip.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
