using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Organizations;
using Workslip.Domain;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Organizations;

public sealed class EfOrganizationRepositoryOnboardingTests
{
    [Fact]
    public async Task CreateAsync_StagesInstallationBaselineWithoutCreatingOrders()
    {
        await using var context = CreateContext();
        var repository = new EfOrganizationRepository(
            context,
            new NoRetryPolicy(),
            new TestCurrentUserContext());
        var request = new CreateOrganizationRequest(
            "New customer tenant",
            "12345678",
            "Customer Admin",
            "admin@example.test",
            null);

        var created = await repository.CreateAsync(request, request.Cvr, CancellationToken.None);

        Assert.NotNull(created);
        var organizationId = created.Organization.Id;
        var definitionIds = context.InstallationTypeDefinitions
            .Where(definition => definition.OrganizationId == organizationId)
            .Select(definition => definition.Id);

        Assert.True(await context.InstallationTypeDefinitions.CountAsync(
            definition => definition.OrganizationId == organizationId) > 0);
        Assert.True(await context.ControlCategoryRow.CountAsync(
            category => category.OrganizationId == organizationId) > 0);
        Assert.True(await context.ControlPointRow.CountAsync(
            controlPoint => controlPoint.OrganizationId == organizationId) > 0);
        Assert.True(await context.InstallationTypeDefinitionMappings.CountAsync(
            mapping => definitionIds.Contains(mapping.InstallationTypeDefinitionId)) > 0);
        Assert.Equal(0, await context.JobReports.CountAsync(report => report.OrganizationId == organizationId));
        Assert.Equal(0, await context.JobReportInstallations.CountAsync(
            installation => installation.OrganizationId == organizationId));
        Assert.Equal(1, await context.Organizations.CountAsync());
        Assert.Equal(1, await context.Users.CountAsync());
    }

    private static SqlDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SqlDbContext(options);
    }

    private sealed class TestCurrentUserContext : ICurrentUserContext
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? OrganizationId => null;
        public string? Role => Roles.Superadmin;
    }

    private sealed class NoRetryPolicy : IDatabaseRetryPolicy
    {
        public Task ExecuteAsync(
            string operationName,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public Task<T> ExecuteAsync<T>(
            string operationName,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }
}
