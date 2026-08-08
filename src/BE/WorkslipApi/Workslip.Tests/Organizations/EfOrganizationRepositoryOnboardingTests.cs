using Microsoft.Data.Sqlite;
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
            new TestCurrentUserContext(),
            new InstallationBaselineProvisioner(context));
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
        Assert.Equal(0, await context.JobReportInstallationCategories.CountAsync());
        Assert.Equal(0, await context.JobReportInstallationControlPoints.CountAsync());
        Assert.Equal(1, await context.Organizations.CountAsync());
        Assert.Equal(1, await context.Users.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenRelationalSaveFails_RollsBackOrganizationAdminAndBaseline()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new SqlDbContext(options))
        {
            await CreateOnboardingSchemaAsync(context);
            var repository = new EfOrganizationRepository(
                context,
                new NoRetryPolicy(),
                new TestCurrentUserContext(),
                new InstallationBaselineProvisioner(context));
            var request = new CreateOrganizationRequest(
                "New customer tenant",
                "12345678",
                "Customer Admin",
                "admin@example.test",
                null);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => repository.CreateAsync(request, request.Cvr, CancellationToken.None));
        }

        await using var verificationContext = new SqlDbContext(options);
        Assert.Equal(0, await verificationContext.Organizations.CountAsync());
        Assert.Equal(0, await verificationContext.Users.CountAsync());
        Assert.Equal(0, await verificationContext.ControlCategoryRow.CountAsync());
        Assert.Equal(0, await verificationContext.ControlPointRow.CountAsync());
        Assert.Equal(0, await verificationContext.InstallationTypeDefinitions.CountAsync());
        Assert.Equal(0, await verificationContext.InstallationTypeDefinitionMappings.CountAsync());
    }

    private static SqlDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SqlDbContext(options);
    }

    private static Task CreateOnboardingSchemaAsync(SqlDbContext context) =>
        context.Database.ExecuteSqlRawAsync("""
            PRAGMA foreign_keys = ON;

            CREATE TABLE Organizations (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                Cvr TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE Users (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                Email TEXT NOT NULL CHECK (Email <> 'admin@example.test'),
                DisplayName TEXT NOT NULL,
                EntraId TEXT NOT NULL,
                EntraEmail TEXT NOT NULL,
                Phone TEXT NOT NULL,
                Role TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (OrganizationId) REFERENCES Organizations (Id)
            );

            CREATE TABLE ControlCategories (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                Name TEXT NOT NULL,
                SortOrder INTEGER NOT NULL
            );

            CREATE TABLE ControlPoints (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                Name TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL
            );

            CREATE TABLE InstallationTypeDefinitions (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                Name TEXT NOT NULL,
                SortOrder INTEGER NOT NULL,
                FOREIGN KEY (OrganizationId) REFERENCES Organizations (Id)
            );

            CREATE TABLE InstallationTypeDefinitionMappings (
                InstallationTypeDefinitionId TEXT NOT NULL,
                ControlCategoryId TEXT NOT NULL,
                ControlPointId TEXT NOT NULL,
                SortOrder INTEGER NOT NULL,
                IsRequired INTEGER NOT NULL,
                PRIMARY KEY (InstallationTypeDefinitionId, ControlCategoryId, ControlPointId),
                FOREIGN KEY (InstallationTypeDefinitionId) REFERENCES InstallationTypeDefinitions (Id),
                FOREIGN KEY (ControlCategoryId) REFERENCES ControlCategories (Id),
                FOREIGN KEY (ControlPointId) REFERENCES ControlPoints (Id)
            );
            """);

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
