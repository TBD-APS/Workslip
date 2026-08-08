using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class InstallationSnapshotTenantConstraintTests
{
    [Fact]
    public void Model_uses_tenant_scoped_snapshot_foreign_keys()
    {
        using var context = CreateModelContext();

        AssertForeignKey<JobReportInstallationCategoryRow, JobReportInstallationRow>(
            context,
            [nameof(JobReportInstallationCategoryRow.OrganizationId), nameof(JobReportInstallationCategoryRow.JobReportInstallationId)],
            [nameof(JobReportInstallationRow.OrganizationId), nameof(JobReportInstallationRow.Id)],
            DeleteBehavior.Cascade);
        AssertForeignKey<JobReportInstallationCategoryRow, ControlCategoryRow>(
            context,
            [nameof(JobReportInstallationCategoryRow.OrganizationId), nameof(JobReportInstallationCategoryRow.ControlCategoryId)],
            [nameof(ControlCategoryRow.OrganizationId), nameof(ControlCategoryRow.Id)],
            DeleteBehavior.Restrict);
        AssertForeignKey<JobReportInstallationControlPointRow, JobReportInstallationCategoryRow>(
            context,
            [nameof(JobReportInstallationControlPointRow.OrganizationId), nameof(JobReportInstallationControlPointRow.JobReportInstallationCategoryId)],
            [nameof(JobReportInstallationCategoryRow.OrganizationId), nameof(JobReportInstallationCategoryRow.Id)],
            DeleteBehavior.Cascade);
        AssertForeignKey<JobReportInstallationControlPointRow, ControlPointRow>(
            context,
            [nameof(JobReportInstallationControlPointRow.OrganizationId), nameof(JobReportInstallationControlPointRow.ControlPointId)],
            [nameof(ControlPointRow.OrganizationId), nameof(ControlPointRow.Id)],
            DeleteBehavior.Restrict);
    }

    [Fact]
    public async Task Category_snapshot_rejects_control_category_from_another_tenant()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var tenants = await SeedTwoTenantsAsync(database.Context);

        database.Context.JobReportInstallationCategories.Add(new JobReportInstallationCategoryRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = tenants.First.OrganizationId,
            JobReportInstallationId = tenants.First.InstallationId,
            ControlCategoryId = tenants.Second.ControlCategoryId,
            SortOrder = 1
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Control_point_snapshot_rejects_control_point_from_another_tenant()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var tenants = await SeedTwoTenantsAsync(database.Context);
        var category = new JobReportInstallationCategoryRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = tenants.First.OrganizationId,
            JobReportInstallationId = tenants.First.InstallationId,
            ControlCategoryId = tenants.First.ControlCategoryId,
            SortOrder = 1
        };
        database.Context.JobReportInstallationCategories.Add(category);
        await database.Context.SaveChangesAsync();

        database.Context.JobReportInstallationControlPoints.Add(new JobReportInstallationControlPointRow
        {
            OrganizationId = tenants.First.OrganizationId,
            JobReportInstallationCategoryId = category.Id,
            ControlPointId = tenants.Second.ControlPointId,
            SortOrder = 1
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Parent_navigation_propagates_tenant_to_new_snapshot_rows()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var tenants = await SeedTwoTenantsAsync(database.Context);
        var installation = await database.Context.JobReportInstallations
            .SingleAsync(row => row.Id == tenants.First.InstallationId);

        var category = new JobReportInstallationCategoryRow
        {
            Id = Guid.NewGuid(),
            JobReportInstallationId = installation.Id,
            JobReportInstallation = installation,
            ControlCategoryId = tenants.First.ControlCategoryId,
            SortOrder = 1
        };
        database.Context.JobReportInstallationCategories.Add(category);
        await database.Context.SaveChangesAsync();

        Assert.Equal(tenants.First.OrganizationId, category.OrganizationId);

        var controlPoint = new JobReportInstallationControlPointRow
        {
            JobReportInstallationCategoryId = category.Id,
            JobReportInstallationCategory = category,
            ControlPointId = tenants.First.ControlPointId,
            SortOrder = 1,
            IsRequired = true
        };
        database.Context.JobReportInstallationControlPoints.Add(controlPoint);
        await database.Context.SaveChangesAsync();

        Assert.Equal(tenants.First.OrganizationId, controlPoint.OrganizationId);
    }

    private static void AssertForeignKey<TDependent, TPrincipal>(
        SqlDbContext context,
        string[] dependentProperties,
        string[] principalProperties,
        DeleteBehavior deleteBehavior)
    {
        var entity = context.Model.FindEntityType(typeof(TDependent));
        Assert.NotNull(entity);

        var foreignKey = Assert.Single(
            entity!.GetForeignKeys(),
            candidate =>
                candidate.PrincipalEntityType.ClrType == typeof(TPrincipal)
                && candidate.Properties.Select(property => property.Name).SequenceEqual(dependentProperties));

        Assert.Equal(
            principalProperties,
            foreignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(deleteBehavior, foreignKey.DeleteBehavior);
    }

    private static SqlDbContext CreateModelContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SqlDbContext(options);
    }

    private static async Task<(TenantSeed First, TenantSeed Second)> SeedTwoTenantsAsync(SqlDbContext context)
    {
        var first = CreateTenant(1);
        var second = CreateTenant(2);

        context.Organizations.AddRange(first.Organization, second.Organization);
        context.JobReports.AddRange(first.Job, second.Job);
        context.InstallationTypeDefinitions.AddRange(first.InstallationDefinition, second.InstallationDefinition);
        context.ControlCategoryRow.AddRange(first.ControlCategory, second.ControlCategory);
        context.ControlPointRow.AddRange(first.ControlPoint, second.ControlPoint);
        context.JobReportInstallations.AddRange(first.Installation, second.Installation);
        await context.SaveChangesAsync();

        return (first.ToSeed(), second.ToSeed());
    }

    private static TenantGraph CreateTenant(int suffix)
    {
        var now = DateTimeOffset.UtcNow;
        var organizationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var installationDefinitionId = Guid.NewGuid();
        var installationId = Guid.NewGuid();
        var controlCategoryId = Guid.NewGuid();
        var controlPointId = Guid.NewGuid();

        var organization = new OrganizationRow
        {
            Id = organizationId,
            Name = $"Tenant {suffix}",
            Cvr = $"1000000{suffix}",
            CreatedAt = now,
            UpdatedAt = now
        };
        var job = new JobReportRow
        {
            Id = jobId,
            OrganizationId = organizationId,
            ReportNumber = $"JOB-{suffix}",
            Status = "Draft",
            CreatedAt = now,
            UpdatedAt = now
        };
        var definition = new InstallationTypeDefinitionRow
        {
            Id = installationDefinitionId,
            OrganizationId = organizationId,
            Name = $"Installation {suffix}",
            SortOrder = 1
        };
        var controlCategory = new ControlCategoryRow
        {
            Id = controlCategoryId,
            OrganizationId = organizationId,
            Name = $"Category {suffix}",
            SortOrder = 1
        };
        var controlPoint = new ControlPointRow
        {
            Id = controlPointId,
            OrganizationId = organizationId,
            Name = $"Point {suffix}",
            SortOrder = 1,
            IsActive = true
        };
        var installation = new JobReportInstallationRow
        {
            Id = installationId,
            OrganizationId = organizationId,
            JobReportId = jobId,
            InstallationTypeDefinitionId = installationDefinitionId,
            SortOrder = 1
        };

        return new TenantGraph(
            organization,
            job,
            definition,
            installation,
            controlCategory,
            controlPoint);
    }

    private sealed record TenantGraph(
        OrganizationRow Organization,
        JobReportRow Job,
        InstallationTypeDefinitionRow InstallationDefinition,
        JobReportInstallationRow Installation,
        ControlCategoryRow ControlCategory,
        ControlPointRow ControlPoint)
    {
        internal TenantSeed ToSeed() => new(
            Organization.Id,
            Installation.Id,
            ControlCategory.Id,
            ControlPoint.Id);
    }

    private sealed record TenantSeed(
        Guid OrganizationId,
        Guid InstallationId,
        Guid ControlCategoryId,
        Guid ControlPointId);

    private sealed class RelationalTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private RelationalTestDatabase(SqliteConnection connection, SqlDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        internal SqlDbContext Context { get; }

        internal static async Task<RelationalTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.CreateFunction(
                "sysutcdatetime",
                () => DateTimeOffset.UtcNow.ToString("O"),
                isDeterministic: false);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<SqlDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new SqlDbContext(options);
            await context.Database.EnsureCreatedAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON;");

            return new RelationalTestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}