using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Application;
using Workslip.Application.Auth;
using Workslip.Application.Organizations;
using Workslip.Domain;
using Workslip.Infrastructure;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class InstallationBaselineProvisionerTests
{
    [Fact]
    public async Task ProvisionAsync_StagesOnlyTenantConsistentReferenceDataWithoutSaving()
    {
        await using var context = CreateContext();
        var organizationId = Guid.NewGuid();
        var provisioner = new InstallationBaselineProvisioner(context);

        var baseline = await provisioner.ProvisionAsync(organizationId);

        Assert.Empty(await context.ControlCategoryRow.AsNoTracking().ToListAsync());
        Assert.Empty(await context.ControlPointRow.AsNoTracking().ToListAsync());
        Assert.Empty(await context.InstallationTypeDefinitions.AsNoTracking().ToListAsync());
        Assert.NotEmpty(baseline.Definitions);
        Assert.NotEmpty(baseline.Mappings);
        Assert.Equal(
            baseline.Mappings.Count,
            context.InstallationTypeDefinitionMappings.Local.Count);

        var categories = context.ControlCategoryRow.Local.ToDictionary(row => row.Id);
        var controlPoints = context.ControlPointRow.Local.ToDictionary(row => row.Id);
        var definitions = context.InstallationTypeDefinitions.Local.ToDictionary(row => row.Id);

        Assert.All(categories.Values, row => Assert.Equal(organizationId, row.OrganizationId));
        Assert.All(controlPoints.Values, row => Assert.Equal(organizationId, row.OrganizationId));
        Assert.All(definitions.Values, row => Assert.Equal(organizationId, row.OrganizationId));
        Assert.All(baseline.Mappings, mapping =>
        {
            Assert.Contains(mapping.InstallationTypeDefinitionId, definitions.Keys);
            Assert.Contains(mapping.ControlCategoryId, categories.Keys);
            Assert.Contains(mapping.ControlPointId, controlPoints.Keys);
        });

        Assert.Empty(context.JobReports.Local);
        Assert.Empty(context.JobReportInstallations.Local);
        Assert.Empty(context.JobReportInstallationCategories.Local);
        Assert.Empty(context.JobReportInstallationControlPoints.Local);
    }

    [Fact]
    public void AddWorkslipInfrastructure_ResolvesRepositoryAndProvisionerAsScopedServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Azure:Sql:ConnectionString"] = "Server=(localdb)\\mssqllocaldb;Database=workslip-di-test;Trusted_Connection=True"
            })
            .Build());
        services.AddSingleton<ICorrelationIdAccessor, TestCorrelationIdAccessor>();
        services.AddScoped<ICurrentUserContext, TestCurrentUserContext>();
        services.AddWorkslipInfrastructure(includeHostedServices: false);

        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        Assert.IsType<EfOrganizationRepository>(
            firstScope.ServiceProvider.GetRequiredService<IOrganizationRepository>());
        var firstProvisioner = firstScope.ServiceProvider.GetRequiredService<InstallationBaselineProvisioner>();
        Assert.Same(
            firstProvisioner,
            firstScope.ServiceProvider.GetRequiredService<InstallationBaselineProvisioner>());
        Assert.NotSame(
            firstProvisioner,
            secondScope.ServiceProvider.GetRequiredService<InstallationBaselineProvisioner>());
    }

    private static SqlDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SqlDbContext(options);
    }

    private sealed class TestCorrelationIdAccessor : ICorrelationIdAccessor
    {
        public string CorrelationId => "test-correlation-id";
    }

    private sealed class TestCurrentUserContext : ICurrentUserContext
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? OrganizationId => null;
        public string? Role => Roles.Superadmin;
    }
}
