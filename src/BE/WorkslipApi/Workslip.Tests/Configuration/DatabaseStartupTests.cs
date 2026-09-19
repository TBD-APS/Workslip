using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Workslip.Api.Configuration;
using Workslip.Domain.Models;
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
            seedDevelopmentData: false,
            seedDevelopmentEntraIdentities: false);
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
            seedDevelopmentData: false,
            seedDevelopmentEntraIdentities: false);
    }

    [Fact]
    public async Task VerifyIfRequiredAsync_OutsideDevelopment_DoesNotMutateIncompleteTenantBaseline()
    {
        var serviceCollection = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        serviceCollection.AddDbContext<SqlDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        await using var services = serviceCollection.BuildServiceProvider();
        await using (var setupScope = services.CreateAsyncScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<SqlDbContext>();
            var timestamp = DateTimeOffset.Parse("2025-01-01T00:00:00Z");
            context.Organizations.Add(new OrganizationRow
            {
                Id = Guid.NewGuid(),
                Name = "Incomplete tenant",
                Cvr = "12345678",
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            });
            await context.SaveChangesAsync();
        }

        await DatabaseStartup.VerifyIfRequiredAsync(
            services,
            BuildConfiguration(generateOpenApiOnly: false),
            seedDevelopmentData: false,
            seedDevelopmentEntraIdentities: false);

        await using var verificationScope = services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<SqlDbContext>();
        Assert.Single(await verificationContext.Organizations.AsNoTracking().ToListAsync());
        Assert.Empty(await verificationContext.ControlCategoryRow.AsNoTracking().ToListAsync());
        Assert.Empty(await verificationContext.ControlPointRow.AsNoTracking().ToListAsync());
        Assert.Empty(await verificationContext.InstallationTypeDefinitions.AsNoTracking().ToListAsync());
        Assert.Empty(await verificationContext.InstallationTypeDefinitionMappings.AsNoTracking().ToListAsync());
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
                seedDevelopmentData: false,
                seedDevelopmentEntraIdentities: false));

        Assert.Contains("SqlDbContext", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyIfRequiredAsync_DbOnlySeed_DoesNotResolveDevelopmentDatabaseSeeder()
    {
        var serviceCollection = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        serviceCollection.AddDbContext<SqlDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        serviceCollection.AddScoped<InstallationBaselineProvisioner>();
        serviceCollection.AddScoped<DevelopmentDatabaseSeeder>(_ => throw new EntraSeedResolvedException());
        await using var services = serviceCollection.BuildServiceProvider();

        await using (var setupScope = services.CreateAsyncScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<SqlDbContext>();
            var timestamp = DateTimeOffset.Parse("2025-01-01T00:00:00Z");
            context.Organizations.Add(new OrganizationRow
            {
                Id = Guid.NewGuid(),
                Name = "Local development tenant",
                Cvr = "12345678",
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            });
            await context.SaveChangesAsync();
        }

        await DatabaseStartup.VerifyIfRequiredAsync(
            services,
            BuildConfiguration(generateOpenApiOnly: false, seedDevelopmentData: true),
            seedDevelopmentData: true,
            seedDevelopmentEntraIdentities: false);

        await using var verificationScope = services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<SqlDbContext>();
        Assert.Equal(3, await verificationContext.Users.CountAsync());
    }

    [Fact]
    public async Task VerifyIfRequiredAsync_EntraOptIn_ResolvesDevelopmentDatabaseSeeder()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDbContext<SqlDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        serviceCollection.AddScoped<DevelopmentDatabaseSeeder>(_ => throw new EntraSeedResolvedException());
        await using var services = serviceCollection.BuildServiceProvider();

        await Assert.ThrowsAsync<EntraSeedResolvedException>(() =>
            DatabaseStartup.VerifyIfRequiredAsync(
                services,
                BuildConfiguration(
                    generateOpenApiOnly: false,
                    seedDevelopmentData: true,
                    seedDevelopmentEntraIdentities: true),
                seedDevelopmentData: true,
                seedDevelopmentEntraIdentities: true));
    }

    [Theory]
    [InlineData("Development", false, false)]
    [InlineData("Development", true, true)]
    [InlineData("Staging", false, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Production", false, false)]
    [InlineData("Production", true, false)]
    public void ShouldSeedDevelopmentData_RequiresDevelopmentAndExplicitOptIn(
        string environmentName,
        bool seedDevelopmentData,
        bool expected)
    {
        var environment = new TestHostEnvironment
        {
            EnvironmentName = environmentName
        };
        var configuration = BuildConfiguration(
            generateOpenApiOnly: false,
            seedDevelopmentData: seedDevelopmentData);

        Assert.Equal(
            expected,
            DatabaseStartup.ShouldSeedDevelopmentData(environment, configuration));
    }

    [Theory]
    [InlineData("Development", false, false, false)]
    [InlineData("Development", false, true, false)]
    [InlineData("Development", true, false, false)]
    [InlineData("Development", true, true, true)]
    [InlineData("Staging", true, true, false)]
    [InlineData("Production", true, true, false)]
    public void ShouldSeedDevelopmentEntraIdentities_RequiresDevelopmentDataAndSeparateOptIn(
        string environmentName,
        bool seedDevelopmentData,
        bool seedDevelopmentEntraIdentities,
        bool expected)
    {
        var environment = new TestHostEnvironment
        {
            EnvironmentName = environmentName
        };
        var configuration = BuildConfiguration(
            generateOpenApiOnly: false,
            seedDevelopmentData: seedDevelopmentData,
            seedDevelopmentEntraIdentities: seedDevelopmentEntraIdentities);

        Assert.Equal(
            expected,
            DatabaseStartup.ShouldSeedDevelopmentEntraIdentities(environment, configuration));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 4)]
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

    private static IConfiguration BuildConfiguration(
        bool generateOpenApiOnly,
        bool seedDevelopmentData = false,
        bool seedDevelopmentEntraIdentities = false) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseStartup.GenerateOpenApiOnlyKey] = generateOpenApiOnly.ToString(),
                [DatabaseStartup.SeedDevelopmentDataKey] = seedDevelopmentData.ToString(),
                [DatabaseStartup.SeedDevelopmentEntraIdentitiesKey] = seedDevelopmentEntraIdentities.ToString()
            })
            .Build();

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Workslip.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class EntraSeedResolvedException : Exception
    {
    }
}
