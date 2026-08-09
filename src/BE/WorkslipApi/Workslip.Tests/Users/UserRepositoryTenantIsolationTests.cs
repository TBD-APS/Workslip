using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Schema;
using Workslip.Tests.Infrastructure;
using Xunit;

namespace Workslip.Tests.Users;

public sealed class UserRepositoryTenantIsolationTests
{
    [Fact]
    public async Task Admin_list_returns_only_non_superadmin_users_from_current_organization()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var admin = CreateUser(organizationA, Roles.Admin, "admin-a@example.test");
        var employee = CreateUser(organizationA, Roles.User, "employee-a@example.test");
        var auditor = CreateUser(organizationA, Roles.Auditor, "auditor-a@example.test");
        var superadmin = CreateUser(organizationA, Roles.Superadmin, "superadmin-a@example.test");
        var foreignAdmin = CreateUser(organizationB, Roles.Admin, "admin-b@example.test");
        var foreignEmployee = CreateUser(organizationB, Roles.User, "employee-b@example.test");

        await using var fixture = await UserRepositoryFixture.CreateAsync(
            [admin, employee, auditor, superadmin, foreignAdmin, foreignEmployee],
            organizationA,
            organizationB,
            admin.Id,
            Roles.Admin);

        var users = await fixture.Repository.GetByOrganizationIdAsync(
            organizationA,
            limit: 100,
            offset: 0,
            search: null,
            sortBy: null,
            sortDirection: null,
            CancellationToken.None);
        var count = await fixture.Repository.GetCountByOrganizationIdAsync(
            organizationA,
            CancellationToken.None);

        Assert.Equal(3, count);
        Assert.Equal(3, users.Count);
        Assert.All(users, user => Assert.Equal(organizationA, user.OrganizationId));
        Assert.DoesNotContain(users, user => user.Role == Roles.Superadmin);
        Assert.DoesNotContain(users, user => user.Id == foreignAdmin.Id || user.Id == foreignEmployee.Id);

        var expectedIds = new HashSet<Guid> { admin.Id, employee.Id, auditor.Id };
        Assert.True(expectedIds.SetEquals(users.Select(user => user.Id)));
    }

    [Fact]
    public async Task Admin_repository_fails_closed_when_a_foreign_organization_is_requested()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var admin = CreateUser(organizationA, Roles.Admin, "admin-a@example.test");
        var foreignEmployee = CreateUser(organizationB, Roles.User, "employee-b@example.test");

        await using var fixture = await UserRepositoryFixture.CreateAsync(
            [admin, foreignEmployee],
            organizationA,
            organizationB,
            admin.Id,
            Roles.Admin);

        var users = await fixture.Repository.GetByOrganizationIdAsync(
            organizationB,
            limit: 100,
            offset: 0,
            search: null,
            sortBy: null,
            sortDirection: null,
            CancellationToken.None);
        var count = await fixture.Repository.GetCountByOrganizationIdAsync(
            organizationB,
            CancellationToken.None);

        Assert.Empty(users);
        Assert.Equal(0, count);
    }

    private static UserDataRow CreateUser(Guid organizationId, string role, string email)
    {
        var now = DateTimeOffset.UtcNow;
        return new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = email,
            DisplayName = email,
            Phone = "+4512345678",
            EntraId = Guid.NewGuid().ToString("N"),
            EntraEmail = email,
            Role = role,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private sealed class UserRepositoryFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly SqlDbContext dbContext;

        private UserRepositoryFixture(
            SqliteConnection connection,
            SqlDbContext dbContext,
            EfUserRepository repository)
        {
            this.connection = connection;
            this.dbContext = dbContext;
            Repository = repository;
        }

        public EfUserRepository Repository { get; }

        public static async Task<UserRepositoryFixture> CreateAsync(
            IReadOnlyCollection<UserDataRow> users,
            Guid organizationA,
            Guid organizationB,
            Guid actorId,
            string actorRole)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<SqlDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(SqliteSchemaCompatibilityInterceptor.Instance)
                .Options;
            var dbContext = new SqlDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var now = DateTimeOffset.UtcNow;
            dbContext.Organizations.AddRange(
                new OrganizationRow
                {
                    Id = organizationA,
                    Name = "Organization A",
                    Cvr = "12345678",
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new OrganizationRow
                {
                    Id = organizationB,
                    Name = "Organization B",
                    Cvr = "87654321",
                    CreatedAt = now,
                    UpdatedAt = now
                });
            dbContext.Users.AddRange(users);
            await dbContext.SaveChangesAsync();

            var currentUser = new TestCurrentUserContext(actorId, organizationA, actorRole);
            return new UserRepositoryFixture(
                connection,
                dbContext,
                new EfUserRepository(dbContext, currentUser));
        }

        public async ValueTask DisposeAsync()
        {
            await dbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;
}
