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

    private static EfOrganizationRepository CreateRepository(SqlDbContext context, Guid organizationId) =>
        new(context, new NoRetryPolicy(), new TestCurrentUserContext(organizationId));

    private static OrganizationRow CreateOrganization() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test organization",
        Cvr = "12345678",
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
