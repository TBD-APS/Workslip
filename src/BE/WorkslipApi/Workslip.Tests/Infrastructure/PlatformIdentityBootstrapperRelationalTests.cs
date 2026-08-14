using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class PlatformIdentityBootstrapperRelationalTests
{
    private const string ConfigKey = "WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL";
    private const string SyntheticEmail = "temporary-superadmin@example.test";
    private static readonly Guid RotatableId =
        new("F6F6F6F6-DA5B-4CC4-BBEB-07B40CAB806F");
    private static readonly Guid[] LegacyIds =
    [
        new("92779E5B-DA5B-4CC4-BBEB-07B40CAB806F"),
        new("D4D4D4D4-DA5B-4CC4-BBEB-07B40CAB806F"),
        new("E5E5E5E5-DA5B-4CC4-BBEB-07B40CAB806F")
    ];

    [Fact]
    public async Task BootstrapAsync_RelationalPathRemovesLegacyRowsAndEphemeralReferences()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        database.Context.Organizations.Add(CreateOrganization(
            PlatformOrganization.Id,
            PlatformOrganization.Name,
            PlatformOrganization.Cvr));
        for (var index = 0; index < LegacyIds.Length; index++)
        {
            database.Context.Users.Add(CreateUser(
                LegacyIds[index],
                PlatformOrganization.Id,
                $"legacy-{index}@example.test",
                $"legacy-entra-{index}"));
        }
        await database.Context.SaveChangesAsync();
        await database.Context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO JobViews (Id, UserId) VALUES ({Guid.NewGuid()}, {LegacyIds[0]})");
        var entra = new FakeSuperadminEntraService();

        await CreateSeeder(database.Context, entra).BootstrapAsync();

        var user = Assert.Single(await database.Context.Users.AsNoTracking().ToListAsync());
        Assert.Equal(RotatableId, user.Id);
        Assert.Equal(SyntheticEmail, user.Email);
        Assert.Equal(Roles.Superadmin, user.Role);
        Assert.Equal(0, await database.Context.JobViews.CountAsync());
        Assert.Equal(3, entra.RevokeCalls.Count);
    }

    [Fact]
    public async Task BootstrapAsync_EntraOwnershipConflictRollsBackDatabaseAndCompensatesCreatedIdentity()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var customer = CreateOrganization(Guid.NewGuid(), "Customer", "12345678");
        database.Context.Organizations.Add(customer);
        database.Context.Users.Add(CreateUser(
            Guid.NewGuid(),
            customer.Id,
            "ordinary@example.test",
            "entra-conflict"));
        await database.Context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService(
            new CreateEntraUserResult(
                "entra-conflict",
                "temporary#EXT#@tenant.onmicrosoft.com",
                "Workslip Test Superadmin",
                Created: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateSeeder(database.Context, entra).BootstrapAsync());

        Assert.False(await database.Context.Organizations.AnyAsync(
            organization => organization.Id == PlatformOrganization.Id));
        Assert.Single(await database.Context.Users.AsNoTracking().ToListAsync());
        Assert.Equal(["entra-conflict"], entra.DeleteCalls);
    }

    private static PlatformIdentityBootstrapper CreateSeeder(
        SqlDbContext context,
        ISuperadminEntraService entra)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfigKey] = SyntheticEmail
            })
            .Build();
        return new PlatformIdentityBootstrapper(
            context,
            entra,
            configuration,
            NullLogger<PlatformIdentityBootstrapper>.Instance);
    }

    private static OrganizationRow CreateOrganization(Guid id, string name, string cvr) => new()
    {
        Id = id,
        Name = name,
        Cvr = cvr,
        CreatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
    };

    private static UserDataRow CreateUser(
        Guid id,
        Guid organizationId,
        string email,
        string entraId) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        FilialId = organizationId,
        Email = email,
        DisplayName = $"Original {id:N}",
        Phone = string.Empty,
        EntraId = entraId,
        EntraEmail = $"{entraId}#EXT#@tenant.onmicrosoft.com",
        Role = Roles.Admin,
        CreatedAt = DateTimeOffset.Parse("2025-01-02T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2025-01-02T00:00:00Z")
    };

    private sealed class FakeSuperadminEntraService(
        CreateEntraUserResult? ensureResult = null) : ISuperadminEntraService
    {
        private readonly CreateEntraUserResult ensureResult = ensureResult ?? new CreateEntraUserResult(
            "entra-temporary-superadmin",
            "temporary-superadmin#EXT#@tenant.onmicrosoft.com",
            "Workslip Test Superadmin",
            Created: false);

        public List<string> RevokeCalls { get; } = [];
        public List<string> DeleteCalls { get; } = [];

        public Task<CreateEntraUserResult> EnsureSuperadminAsync(
            string email,
            string displayName,
            CancellationToken cancellationToken) => Task.FromResult(ensureResult);

        public Task RevokeSuperadminAsync(string entraUserId, CancellationToken cancellationToken)
        {
            RevokeCalls.Add(entraUserId);
            return Task.CompletedTask;
        }

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct)
        {
            DeleteCalls.Add(entraUserId);
            return Task.CompletedTask;
        }

        public Task<CreateEntraUserResult> CreateUserAsync(
            string email,
            string displayName,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(
            string email,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<CreateEntraUserResult> InviteAdminAsync(
            string email,
            string displayName,
            CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RelationalTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private RelationalTestDatabase(SqliteConnection connection, SqlDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        internal SqlDbContext Context { get; }

        internal static async Task<RelationalTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SqlDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new SqlDbContext(options);
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE Organizations (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Cvr TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE UNIQUE INDEX UX_Organizations_Cvr ON Organizations (Cvr);

                CREATE TABLE Users (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL,
                    FilialId TEXT NOT NULL,
                    Email TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    EntraId TEXT NOT NULL,
                    EntraEmail TEXT NOT NULL,
                    Phone TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    UserKind TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (OrganizationId) REFERENCES Organizations (Id)
                );
                CREATE UNIQUE INDEX UX_Users_Organization_Id ON Users (OrganizationId, Id);

                CREATE TABLE Customers (Id TEXT NOT NULL PRIMARY KEY, OrganizationId TEXT NOT NULL);
                CREATE TABLE JobReports (Id TEXT NOT NULL PRIMARY KEY, OrganizationId TEXT NOT NULL);
                CREATE TABLE JobAssignments (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL,
                    UserId TEXT NOT NULL,
                    AssignedByUserId TEXT NULL
                );
                CREATE TABLE JobReportLinks (Id TEXT NOT NULL PRIMARY KEY, OrganizationId TEXT NOT NULL);
                CREATE TABLE JobEvents (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL,
                    ActorId TEXT NULL
                );
                CREATE TABLE InviteTokens (Id TEXT NOT NULL PRIMARY KEY, OrganizationId TEXT NOT NULL);
                CREATE TABLE Worksheets (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL,
                    UserId TEXT NOT NULL
                );
                CREATE TABLE PushSubscriptions (Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL);
                CREATE TABLE NotificationQueue (Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL);
                CREATE TABLE JobViews (Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL);
                CREATE TABLE JobClosureFlags (
                    Id TEXT NOT NULL PRIMARY KEY,
                    NormalizedLabel TEXT NOT NULL,
                    Label TEXT NOT NULL,
                    IsExclusive INTEGER NOT NULL,
                    IsActive INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE TABLE JobReportClosureFlags (
                    Id TEXT NOT NULL PRIMARY KEY,
                    JobReportId TEXT NOT NULL,
                    OrganizationId TEXT NOT NULL,
                    ClosureFlagId TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL
                );
                CREATE TABLE JobReportInstallations (Id TEXT NOT NULL PRIMARY KEY, OrganizationId TEXT NOT NULL);
                CREATE TABLE ControlCategories (Id TEXT NOT NULL PRIMARY KEY, OrganizationId TEXT NOT NULL);
                CREATE TABLE ControlPoints (Id TEXT NOT NULL PRIMARY KEY, OrganizationId TEXT NOT NULL);
                CREATE TABLE InstallationTypeDefinitions (Id TEXT NOT NULL PRIMARY KEY, OrganizationId TEXT NOT NULL);
                """);
            return new RelationalTestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
