using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        var installation = new JobReportInstallationRow
        {
            Id = tenants.First.InstallationId,
            OrganizationId = tenants.First.OrganizationId
        };
        database.Context.Attach(installation);

        var category = new JobReportInstallationCategoryRow
        {
            Id = Guid.NewGuid(),
            JobReportInstallationId = installation.Id,
            JobReportInstallation = installation,
            ControlCategoryId = tenants.First.ControlCategoryId,
            SortOrder = 1
        };
        database.Context.JobReportInstallationCategories.Add(category);

        Assert.Equal(tenants.First.OrganizationId, category.OrganizationId);
        await database.Context.SaveChangesAsync();

        var controlPoint = new JobReportInstallationControlPointRow
        {
            JobReportInstallationCategoryId = category.Id,
            JobReportInstallationCategory = category,
            ControlPointId = tenants.First.ControlPointId,
            SortOrder = 1,
            IsRequired = true
        };
        database.Context.JobReportInstallationControlPoints.Add(controlPoint);

        Assert.Equal(tenants.First.OrganizationId, controlPoint.OrganizationId);
        await database.Context.SaveChangesAsync();
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

        Assert.Equal(principalProperties, foreignKey.PrincipalKey.Properties.Select(property => property.Name));
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
        var first = TenantSeed.Create();
        var second = TenantSeed.Create();
        await SeedTenantAsync(context, first);
        await SeedTenantAsync(context, second);
        return (first, second);
    }

    private static Task SeedTenantAsync(SqlDbContext context, TenantSeed seed) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO JobReportInstallations (Id, OrganizationId)
            VALUES ({seed.InstallationId}, {seed.OrganizationId});
            INSERT INTO ControlCategories (Id, OrganizationId)
            VALUES ({seed.ControlCategoryId}, {seed.OrganizationId});
            INSERT INTO ControlPoints (Id, OrganizationId)
            VALUES ({seed.ControlPointId}, {seed.OrganizationId});
            """);

    private sealed record TenantSeed(
        Guid OrganizationId,
        Guid InstallationId,
        Guid ControlCategoryId,
        Guid ControlPointId)
    {
        internal static TenantSeed Create() => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
    }

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
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<SqlDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new SqlDbContext(options);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE JobReportInstallations (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL,
                    UNIQUE (OrganizationId, Id)
                );
                CREATE TABLE ControlCategories (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL,
                    UNIQUE (OrganizationId, Id)
                );
                CREATE TABLE ControlPoints (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL,
                    UNIQUE (OrganizationId, Id)
                );
                CREATE TABLE JobReportInstallationCategories (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL,
                    JobReportInstallationId TEXT NOT NULL,
                    ControlCategoryId TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    IsIrrelevant INTEGER NOT NULL DEFAULT 0,
                    UNIQUE (OrganizationId, Id),
                    UNIQUE (OrganizationId, JobReportInstallationId, ControlCategoryId),
                    FOREIGN KEY (OrganizationId, JobReportInstallationId)
                        REFERENCES JobReportInstallations (OrganizationId, Id) ON DELETE CASCADE,
                    FOREIGN KEY (OrganizationId, ControlCategoryId)
                        REFERENCES ControlCategories (OrganizationId, Id) ON DELETE RESTRICT
                );
                CREATE TABLE JobReportInstallationControlPoints (
                    OrganizationId TEXT NOT NULL,
                    JobReportInstallationCategoryId TEXT NOT NULL,
                    ControlPointId TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    IsRequired INTEGER NOT NULL DEFAULT 0,
                    IsChecked INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (JobReportInstallationCategoryId, ControlPointId),
                    FOREIGN KEY (OrganizationId, JobReportInstallationCategoryId)
                        REFERENCES JobReportInstallationCategories (OrganizationId, Id) ON DELETE CASCADE,
                    FOREIGN KEY (OrganizationId, ControlPointId)
                        REFERENCES ControlPoints (OrganizationId, Id) ON DELETE RESTRICT
                );
                """;
            await command.ExecuteNonQueryAsync();

            return new RelationalTestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
