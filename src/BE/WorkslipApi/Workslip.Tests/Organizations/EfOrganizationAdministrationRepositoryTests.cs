using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Organizations;

public sealed class EfOrganizationAdministrationRepositoryTests
{
    [Fact]
    public async Task ListOrganizationsAsync_DoesNotFilterReservedCvrOnDifferentId()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var customerWithReservedCvr = CreateOrganization(Guid.NewGuid(), PlatformOrganization.Cvr);
        var ordinaryCustomer = CreateOrganization(Guid.NewGuid(), "87654321");
        database.Context.Organizations.AddRange(customerWithReservedCvr, ordinaryCustomer);
        await database.Context.SaveChangesAsync();

        var repository = CreateRepository(database.Context, ordinaryCustomer.Id);

        var organizations = await repository.ListOrganizationsAsync(CancellationToken.None);

        Assert.Contains(organizations, organization => organization.Id == customerWithReservedCvr.Id);
        Assert.Contains(organizations, organization => organization.Id == ordinaryCustomer.Id);
    }

    [Fact]
    public async Task GetOrganizationAsync_WhenIdIsReserved_ReturnsNull()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var platform = CreateOrganization(PlatformOrganization.Id, PlatformOrganization.Cvr);
        database.Context.Organizations.Add(platform);
        await database.Context.SaveChangesAsync();

        var repository = CreateRepository(database.Context, PlatformOrganization.Id);

        var organization = await repository.GetOrganizationAsync(
            PlatformOrganization.Id,
            CancellationToken.None);

        Assert.Null(organization);
    }

    [Fact]
    public async Task UpdateAdminAsync_WhenObservedStateChanged_DoesNotOverwriteCurrentRow()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organization = CreateOrganization();
        var admin = CreateUser(organization.Id, Roles.Admin);
        database.Context.Organizations.Add(organization);
        database.Context.Users.Add(admin);
        await database.Context.SaveChangesAsync();

        var repository = CreateRepository(database.Context, organization.Id);
        var observed = await repository.GetUserByEmailAsync(admin.Email, CancellationToken.None);
        Assert.NotNull(observed);

        var tracked = await database.Context.Users.SingleAsync(user => user.Id == admin.Id);
        tracked.EntraId = "entra-concurrent";
        tracked.DisplayName = "Concurrent update";
        await database.Context.SaveChangesAsync();

        var requested = CopyUser(observed);
        requested.DisplayName = "Superadmin request";
        requested.EntraId = "entra-request";
        requested.UpdatedAt = DateTimeOffset.UtcNow;

        var updated = await repository.UpdateAdminAsync(
            requested,
            expectedEmail: observed.Email,
            expectedEntraId: observed.EntraId,
            CancellationToken.None);

        Assert.False(updated);
        var stored = await database.Context.Users.AsNoTracking().SingleAsync(user => user.Id == admin.Id);
        Assert.Equal("Concurrent update", stored.DisplayName);
        Assert.Equal("entra-concurrent", stored.EntraId);
    }

    [Fact]
    public async Task UpdateAdminAsync_WhenUserBecameSuperAdmin_DoesNotDemoteUser()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organization = CreateOrganization();
        var user = CreateUser(organization.Id, Roles.Superadmin);
        database.Context.Organizations.Add(organization);
        database.Context.Users.Add(user);
        await database.Context.SaveChangesAsync();

        var repository = CreateRepository(database.Context, organization.Id);
        var requested = CopyUser(user);
        requested.Role = Roles.Admin;
        requested.DisplayName = "Attempted demotion";
        requested.UpdatedAt = DateTimeOffset.UtcNow;

        var updated = await repository.UpdateAdminAsync(
            requested,
            expectedEmail: user.Email,
            expectedEntraId: user.EntraId,
            CancellationToken.None);

        Assert.False(updated);
        var stored = await database.Context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal(Roles.Superadmin, stored.Role);
        Assert.NotEqual("Attempted demotion", stored.DisplayName);
    }

    [Fact]
    public async Task ListUsersAsync_ExcludesPlatformOrganizationUsers()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var platform = CreateOrganization(PlatformOrganization.Id, PlatformOrganization.Cvr);
        var ordinary = CreateOrganization();
        var superadmin = CreateUser(platform.Id, Roles.Superadmin);
        var admin = CreateUser(ordinary.Id, Roles.Admin);
        database.Context.Organizations.AddRange(platform, ordinary);
        database.Context.Users.AddRange(superadmin, admin);
        await database.Context.SaveChangesAsync();

        var repository = CreateRepository(database.Context, ordinary.Id);

        var users = await repository.ListUsersAsync(
            organizationId: null, limit: 50, offset: 0, search: null, sortBy: "displayName", sortDirection: "asc", CancellationToken.None);

        Assert.Single(users);
        Assert.Equal(admin.Id, users[0].User.Id);
    }

    [Fact]
    public async Task ListUsersAsync_FiltersByOrganizationId()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var first = CreateOrganization();
        var second = CreateOrganization();
        var userInFirst = CreateUser(first.Id, Roles.User);
        var userInSecond = CreateUser(second.Id, Roles.User);
        database.Context.Organizations.AddRange(first, second);
        database.Context.Users.AddRange(userInFirst, userInSecond);
        await database.Context.SaveChangesAsync();

        var repository = CreateRepository(database.Context, first.Id);

        var users = await repository.ListUsersAsync(
            organizationId: first.Id, limit: 50, offset: 0, search: null, sortBy: "displayName", sortDirection: "asc", CancellationToken.None);

        Assert.Single(users);
        Assert.Equal(userInFirst.Id, users[0].User.Id);
        Assert.Equal(first.Name, users[0].OrganizationName);
    }

    [Fact]
    public async Task CreateUserAsync_PersistsUserInRequestedOrganization()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organization = CreateOrganization();
        database.Context.Organizations.Add(organization);
        await database.Context.SaveChangesAsync();

        var repository = CreateRepository(database.Context, organization.Id);
        var user = CreateUser(organization.Id, Roles.User);

        var createdId = await repository.CreateUserAsync(user, CancellationToken.None);

        Assert.Equal(user.Id, createdId);
        var stored = await database.Context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal(organization.Id, stored.OrganizationId);
        Assert.Equal(Roles.User, stored.Role);
    }

    [Fact]
    public async Task UpdateUserAsync_WhenObservedStateChanged_DoesNotOverwriteCurrentRow()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organization = CreateOrganization();
        var user = CreateUser(organization.Id, Roles.User);
        database.Context.Organizations.Add(organization);
        database.Context.Users.Add(user);
        await database.Context.SaveChangesAsync();

        var repository = CreateRepository(database.Context, organization.Id);
        var observed = await repository.GetUserWithOrganizationAsync(user.Id, CancellationToken.None);
        Assert.NotNull(observed);

        var tracked = await database.Context.Users.SingleAsync(candidate => candidate.Id == user.Id);
        tracked.EntraId = "entra-concurrent";
        await database.Context.SaveChangesAsync();

        var requested = CopyUser(observed.User);
        requested.Role = Roles.Auditor;
        requested.UpdatedAt = DateTimeOffset.UtcNow;

        var updated = await repository.UpdateUserAsync(
            requested,
            expectedEmail: observed.User.Email,
            expectedEntraId: observed.User.EntraId,
            CancellationToken.None);

        Assert.False(updated);
        var stored = await database.Context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal(Roles.User, stored.Role);
    }

    [Fact]
    public async Task UpdateUserAsync_CanPromoteAndDemoteAnyRole()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organization = CreateOrganization();
        var user = CreateUser(organization.Id, Roles.User);
        database.Context.Organizations.Add(organization);
        database.Context.Users.Add(user);
        await database.Context.SaveChangesAsync();

        var repository = CreateRepository(database.Context, organization.Id);
        var requested = CopyUser(user);
        requested.Role = Roles.Superadmin;
        requested.UpdatedAt = DateTimeOffset.UtcNow;

        var updated = await repository.UpdateUserAsync(
            requested,
            expectedEmail: user.Email,
            expectedEntraId: user.EntraId,
            CancellationToken.None);

        Assert.True(updated);
        var stored = await database.Context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal(Roles.Superadmin, stored.Role);
    }

    [Fact]
    public async Task DeleteUserAsync_RemovesUser()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organization = CreateOrganization();
        var user = CreateUser(organization.Id, Roles.User);
        database.Context.Organizations.Add(organization);
        database.Context.Users.Add(user);
        await database.Context.SaveChangesAsync();

        var repository = CreateRepository(database.Context, organization.Id);

        var deleted = await repository.DeleteUserAsync(user.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.False(await database.Context.Users.AnyAsync(candidate => candidate.Id == user.Id));
    }

    [Fact]
    public async Task DeleteUserAsync_WhenUserBelongsToPlatformOrganization_DoesNotDelete()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var platform = CreateOrganization(PlatformOrganization.Id, PlatformOrganization.Cvr);
        var superadmin = CreateUser(platform.Id, Roles.Superadmin);
        database.Context.Organizations.Add(platform);
        database.Context.Users.Add(superadmin);
        await database.Context.SaveChangesAsync();

        var repository = CreateRepository(database.Context, platform.Id);

        var deleted = await repository.DeleteUserAsync(superadmin.Id, CancellationToken.None);

        Assert.False(deleted);
        Assert.True(await database.Context.Users.AnyAsync(candidate => candidate.Id == superadmin.Id));
    }

    private static EfOrganizationRepository CreateRepository(SqlDbContext context, Guid organizationId) =>
        new(
            context,
            new NoRetryPolicy(),
            new TestCurrentUserContext(organizationId),
            new InstallationBaselineProvisioner(context));

    private static OrganizationRow CreateOrganization(
        Guid? id = null,
        string cvr = "12345678") => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "Test organization",
        Cvr = cvr,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static UserDataRow CreateUser(Guid organizationId, string role) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Email = $"{Guid.NewGuid():N}@example.test",
        DisplayName = "Original admin",
        Phone = string.Empty,
        EntraEmail = "entra@example.test",
        EntraId = "entra-original",
        Role = role,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static UserDataRow CopyUser(UserDataRow user) => new()
    {
        Id = user.Id,
        OrganizationId = user.OrganizationId,
        FilialId = user.FilialId,
        Email = user.Email,
        DisplayName = user.DisplayName,
        Phone = user.Phone,
        EntraEmail = user.EntraEmail,
        EntraId = user.EntraId,
        Role = user.Role,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };

    private sealed class TestCurrentUserContext(Guid organizationId) : ICurrentUserContext
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? OrganizationId => organizationId;
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

                CREATE TABLE Users (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL,
                    FilialId TEXT NOT NULL,
                    Email TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    Phone TEXT NOT NULL,
                    EntraEmail TEXT NOT NULL,
                    EntraId TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (OrganizationId) REFERENCES Organizations (Id)
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
