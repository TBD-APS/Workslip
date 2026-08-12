using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Users;

public sealed class EfSuperAdminUserRepositoryTests
{
    [Fact]
    public async Task ListAsync_ReturnsUsersAcrossTenantOrganizationsWithAudienceButExcludesPlatformUsers()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organizationA = CreateOrganization("Organization A");
        var organizationB = CreateOrganization("Organization B");
        var platform = CreateOrganization("Platform", PlatformOrganization.Id, PlatformOrganization.Cvr);
        var filialA = CreateFilial(organizationA, "A filial");
        var filialB = CreateFilial(organizationB, "B filial");
        var platformFilial = CreateFilial(platform, "Platform", PlatformOrganization.Id);

        database.Context.Organizations.AddRange(organizationA, organizationB, platform);
        database.Context.Set<OrganizationFilialRow>().AddRange(filialA, filialB, platformFilial);
        database.Context.Users.AddRange(
            CreateUser(organizationA, filialA, "a@example.test", Roles.User, UserKinds.Member),
            CreateUser(organizationB, filialB, "b@example.test", Roles.Admin, UserKinds.InternalTest),
            CreateUser(platform, platformFilial, "platform@example.test", Roles.Superadmin, UserKinds.Member));
        await database.Context.SaveChangesAsync();

        var repository = new EfSuperAdminUserRepository(database.Context, new NoRetryPolicy());

        var users = await repository.ListAsync(50, 0, null, "organization", "asc", CancellationToken.None);

        Assert.Equal(2, users.Count);
        Assert.Contains(users, user =>
            user.OrganizationId == organizationA.Id
            && user.FilialId == filialA.Id
            && user.UserKind == UserKinds.Member);
        Assert.Contains(users, user =>
            user.OrganizationId == organizationB.Id
            && user.FilialId == filialB.Id
            && user.UserKind == UserKinds.InternalTest);
        Assert.DoesNotContain(users, user => user.OrganizationId == PlatformOrganization.Id);
    }

    [Fact]
    public async Task UpdateAsync_CanReclassifyTenantUserAudience()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organization = CreateOrganization("Organization A");
        var filial = CreateFilial(organization, "A filial");
        var user = CreateUser(organization, filial, "a@example.test", Roles.User, UserKinds.Member);
        database.Context.Organizations.Add(organization);
        database.Context.Set<OrganizationFilialRow>().Add(filial);
        database.Context.Users.Add(user);
        await database.Context.SaveChangesAsync();

        var repository = new EfSuperAdminUserRepository(database.Context, new NoRetryPolicy());

        var updated = await repository.UpdateAsync(
            user.Id,
            user.DisplayName,
            user.Phone,
            user.Role,
            filial.Id,
            UserKinds.InternalTest,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(updated);
        Assert.Equal(
            UserKinds.InternalTest,
            (await database.Context.Users.AsNoTracking().SingleAsync()).UserKind);
    }

    [Fact]
    public async Task TenantFilialExistsAsync_WhenFilialBelongsToAnotherOrganization_ReturnsFalse()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organizationA = CreateOrganization("Organization A");
        var organizationB = CreateOrganization("Organization B");
        var filialB = CreateFilial(organizationB, "B filial");
        database.Context.Organizations.AddRange(organizationA, organizationB);
        database.Context.Set<OrganizationFilialRow>().Add(filialB);
        await database.Context.SaveChangesAsync();

        var repository = new EfSuperAdminUserRepository(database.Context, new NoRetryPolicy());

        var exists = await repository.TenantFilialExistsAsync(
            organizationA.Id,
            filialB.Id,
            CancellationToken.None);

        Assert.False(exists);
    }

    private static OrganizationRow CreateOrganization(
        string name,
        Guid? id = null,
        string? cvr = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Cvr = cvr ?? Random.Shared.Next(10_000_000, 99_999_999).ToString(),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static OrganizationFilialRow CreateFilial(
        OrganizationRow organization,
        string name,
        Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        OrganizationId = organization.Id,
        Organization = organization,
        Name = name,
        IsDefault = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static UserDataRow CreateUser(
        OrganizationRow organization,
        OrganizationFilialRow filial,
        string email,
        string role,
        string userKind) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organization.Id,
        FilialId = filial.Id,
        Email = email,
        DisplayName = email,
        Phone = string.Empty,
        EntraEmail = email,
        EntraId = Guid.NewGuid().ToString(),
        Role = role,
        UserKind = userKind,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

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
                PRAGMA foreign_keys = ON;

                CREATE TABLE Organizations (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Cvr TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE OrganizationFilials (
                    Id TEXT NOT NULL PRIMARY KEY,
                    OrganizationId TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    IsDefault INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (OrganizationId) REFERENCES Organizations (Id)
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
                    UserKind TEXT NOT NULL DEFAULT 'Member',
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
