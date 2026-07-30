using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class DevelopmentDatabaseSeederRelationalTests
{
    private static readonly Guid CanonicalRasmusId =
        new("92779E5B-DA5B-4CC4-BBEB-07B40CAB806F");

    private static readonly Guid CanonicalMahadId =
        new("D4D4D4D4-DA5B-4CC4-BBEB-07B40CAB806F");

    [Fact]
    public async Task SeedAsync_MovesCanonicalRowsThroughRelationalAffectedRowUpdates()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var customer = CreateOrganization();
        database.Context.Organizations.Add(customer);
        database.Context.Users.AddRange(
            CreateCanonicalUser(CanonicalRasmusId, customer.Id, "rasmusvm6@hotmail.com", "Old Rasmus"),
            CreateCanonicalUser(CanonicalMahadId, customer.Id, "mahad8@outlook.dk", "Old Mahad"));
        await database.Context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService();

        await CreateSeeder(database.Context, entra).SeedAsync();

        var canonicalUsers = await database.Context.Users
            .AsNoTracking()
            .Where(user => user.Id == CanonicalRasmusId || user.Id == CanonicalMahadId)
            .OrderBy(user => user.Id)
            .ToListAsync();
        Assert.Equal(2, canonicalUsers.Count);
        Assert.All(canonicalUsers, user => Assert.Equal(PlatformOrganization.Id, user.OrganizationId));
        Assert.All(canonicalUsers, user => Assert.Equal(Roles.Superadmin, user.Role));
        Assert.Equal("Rasmus Bak Jakobsen", canonicalUsers.Single(user => user.Id == CanonicalRasmusId).DisplayName);
        Assert.Equal("Mahad", canonicalUsers.Single(user => user.Id == CanonicalMahadId).DisplayName);
        Assert.Equal(5, await database.Context.Users.CountAsync());
        Assert.Equal(2, await database.Context.Organizations.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_WhenSecondGraphEnsureFails_RollsBackLocalSeedAndCompensatesFirstIdentity()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var customer = CreateOrganization();
        database.Context.Organizations.Add(customer);
        await database.Context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService((email, call) =>
            call == 1
                ? Task.FromResult(CreateEntraResult(email, created: true))
                : throw new InvalidOperationException("Second Graph ensure failed."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSeeder(database.Context, entra).SeedAsync());

        Assert.Equal(["entra-rasmus"], entra.DeleteCalls);
        Assert.Empty(database.Context.ChangeTracker.Entries());
        Assert.Single(await database.Context.Organizations.AsNoTracking().ToListAsync());
        Assert.Equal(customer.Id, (await database.Context.Organizations.SingleAsync()).Id);
        Assert.Empty(await database.Context.Users.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SeedAsync_WhenCreatedGraphIdentityDuplicatesExistingResultByCase_CompensatesBeforeFailing()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var customer = CreateOrganization();
        database.Context.Organizations.Add(customer);
        await database.Context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService((email, call) =>
            Task.FromResult(new CreateEntraUserResult(
                call == 1 ? "entra-shared" : "ENTRA-SHARED",
                $"{email}#EXT#@tenant.onmicrosoft.com",
                email,
                Created: call == 2)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSeeder(database.Context, entra).SeedAsync());

        Assert.Contains("more than one", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["ENTRA-SHARED"], entra.DeleteCalls);
        Assert.Empty(database.Context.ChangeTracker.Entries());
        Assert.Single(await database.Context.Organizations.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.Users.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task SeedAsync_WhenCanonicalPredicateChanges_RollsBackAndCompensatesInReverseOrder()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var customer = CreateOrganization();
        database.Context.Organizations.Add(customer);
        database.Context.Users.AddRange(
            CreateCanonicalUser(CanonicalRasmusId, customer.Id, "rasmusvm6@hotmail.com", "Observed Rasmus"),
            CreateCanonicalUser(CanonicalMahadId, customer.Id, "mahad8@outlook.dk", "Observed Mahad"));
        await database.Context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService(async (email, call) =>
        {
            if (call == 2)
            {
                await database.Context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Users SET DisplayName = {"Changed after preflight"} WHERE Id = {CanonicalRasmusId}");
            }

            return CreateEntraResult(email, created: true);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSeeder(database.Context, entra).SeedAsync());

        Assert.Equal(["entra-mahad", "entra-rasmus"], entra.DeleteCalls);
        Assert.False(await database.Context.Organizations.AnyAsync(
            organization => organization.Id == PlatformOrganization.Id));
        var canonicalUsers = await database.Context.Users
            .AsNoTracking()
            .Where(user => user.Id == CanonicalRasmusId || user.Id == CanonicalMahadId)
            .ToListAsync();
        Assert.All(canonicalUsers, user => Assert.Equal(customer.Id, user.OrganizationId));
        Assert.Equal(
            "Observed Rasmus",
            canonicalUsers.Single(user => user.Id == CanonicalRasmusId).DisplayName);
        Assert.Equal(
            "Observed Mahad",
            canonicalUsers.Single(user => user.Id == CanonicalMahadId).DisplayName);
        Assert.Equal(2, canonicalUsers.Count);
    }

    private static DevelopmentDatabaseSeeder CreateSeeder(
        SqlDbContext context,
        ISuperadminEntraService entra) =>
        new(context, entra, NullLogger<DevelopmentDatabaseSeeder>.Instance);

    private static OrganizationRow CreateOrganization() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Customer tenant",
        Cvr = "12345678",
        CreatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
    };

    private static UserDataRow CreateCanonicalUser(
        Guid id,
        Guid organizationId,
        string email,
        string displayName) =>
        new()
        {
            Id = id,
            OrganizationId = organizationId,
            Email = email,
            DisplayName = displayName,
            Phone = "old-phone",
            EntraId = "old-entra",
            EntraEmail = "old-entra@example.test",
            Role = Roles.Admin,
            CreatedAt = DateTimeOffset.Parse("2025-01-02T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2025-01-02T00:00:00Z")
        };

    private static CreateEntraUserResult CreateEntraResult(string email, bool created)
    {
        var localPart = email.StartsWith("rasmus", StringComparison.Ordinal)
            ? "rasmus"
            : "mahad";
        return new CreateEntraUserResult(
            $"entra-{localPart}",
            $"{localPart}#EXT#@tenant.onmicrosoft.com",
            localPart,
            created);
    }

    private sealed class FakeSuperadminEntraService(
        Func<string, int, Task<CreateEntraUserResult>>? ensure = null)
        : ISuperadminEntraService
    {
        private readonly Func<string, int, Task<CreateEntraUserResult>> ensure =
            ensure ?? ((email, _) => Task.FromResult(CreateEntraResult(email, created: false)));
        private int ensureCallCount;

        public List<string> DeleteCalls { get; } = [];

        public Task<CreateEntraUserResult> EnsureSuperadminAsync(
            string email,
            string displayName,
            CancellationToken cancellationToken) =>
            ensure(email, ++ensureCallCount);

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct)
        {
            DeleteCalls.Add(entraUserId);
            return Task.CompletedTask;
        }

        public Task<CreateEntraUserResult> CreateUserAsync(
            string email,
            string displayName,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(
            string email,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CreateEntraUserResult> InviteAdminAsync(
            string email,
            string displayName,
            CancellationToken ct) =>
            throw new NotSupportedException();
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
                    Email TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    EntraId TEXT NOT NULL,
                    EntraEmail TEXT NOT NULL,
                    Phone TEXT NOT NULL,
                    Role TEXT NOT NULL,
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

                CREATE TABLE JobReportInstallations (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL
                );
                CREATE TABLE ControlCategories (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL
                );
                CREATE TABLE ControlPoints (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL
                );
                CREATE TABLE InstallationTypeDefinitions (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL
                );
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
