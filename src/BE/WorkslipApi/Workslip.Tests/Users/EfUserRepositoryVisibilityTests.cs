using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Schema;

namespace Workslip.Tests.Users;

public sealed class EfUserRepositoryVisibilityTests
{
    [Fact]
    public async Task Member_admin_list_and_count_exclude_internal_test_and_superadmin_users()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var actor = CreateUser(organizationId, filialId, Roles.Admin, UserKinds.Member, "member-admin@example.test");
        var member = CreateUser(organizationId, filialId, Roles.User, UserKinds.Member, "member@example.test");
        var internalTest = CreateUser(organizationId, filialId, Roles.User, UserKinds.InternalTest, "internal@example.test");
        var superadmin = CreateUser(organizationId, filialId, Roles.Superadmin, UserKinds.Member, "superadmin@example.test");
        database.Context.Users.AddRange(actor, member, internalTest, superadmin);
        await database.Context.SaveChangesAsync();

        var repository = new EfUserRepository(
            database.Context,
            new TestCurrentUserContext(actor.Id, organizationId, Roles.Admin));

        var users = await repository.GetByOrganizationIdAsync(
            organizationId, 50, 0, null, "displayName", "asc", CancellationToken.None);
        var count = await repository.GetCountByOrganizationIdAsync(organizationId, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(2, users.Count);
        Assert.Contains(users, user => user.Id == actor.Id);
        Assert.Contains(users, user => user.Id == member.Id);
        Assert.DoesNotContain(users, user => user.Id == internalTest.Id);
        Assert.DoesNotContain(users, user => user.Id == superadmin.Id);
        Assert.Null(await repository.GetByIdAsync(internalTest.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Internal_test_admin_list_and_count_return_only_internal_test_audience()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var actor = CreateUser(organizationId, filialId, Roles.Admin, UserKinds.InternalTest, "test-admin@example.test");
        var testUser = CreateUser(organizationId, filialId, Roles.User, UserKinds.InternalTest, "test-user@example.test");
        var member = CreateUser(organizationId, filialId, Roles.User, UserKinds.Member, "member@example.test");
        database.Context.Users.AddRange(actor, testUser, member);
        await database.Context.SaveChangesAsync();

        var repository = new EfUserRepository(
            database.Context,
            new TestCurrentUserContext(actor.Id, organizationId, Roles.Admin));

        var users = await repository.GetByOrganizationIdAsync(
            organizationId, 50, 0, null, "displayName", "asc", CancellationToken.None);
        var count = await repository.GetCountByOrganizationIdAsync(organizationId, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.All(users, user => Assert.Equal(UserKinds.InternalTest, user.UserKind));
        Assert.Contains(users, user => user.Id == testUser.Id);
        Assert.DoesNotContain(users, user => user.Id == member.Id);
        Assert.NotNull(await repository.GetByIdAsync(testUser.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Superadmin_list_can_see_both_audiences()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var member = CreateUser(organizationId, filialId, Roles.User, UserKinds.Member, "member@example.test");
        var internalTest = CreateUser(organizationId, filialId, Roles.Admin, UserKinds.InternalTest, "internal@example.test");
        database.Context.Users.AddRange(member, internalTest);
        await database.Context.SaveChangesAsync();

        var repository = new EfUserRepository(
            database.Context,
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, Roles.Superadmin));

        var users = await repository.GetByOrganizationIdAsync(
            organizationId, 50, 0, null, "displayName", "asc", CancellationToken.None);

        Assert.Contains(users, user => user.Id == member.Id);
        Assert.Contains(users, user => user.Id == internalTest.Id);
        Assert.NotNull(await repository.GetByIdAsync(internalTest.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Authentication_lookup_preserves_filial_and_user_kind_without_audience_filtering()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var user = CreateUser(
            organizationId,
            filialId,
            Roles.User,
            UserKinds.InternalTest,
            "auth@example.test");
        user.EntraId = "entra-auth";
        database.Context.Users.Add(user);
        await database.Context.SaveChangesAsync();

        var repository = new EfUserRepository(
            database.Context,
            new TestCurrentUserContext(null, null, null));

        var result = await repository.GetByExternalIdentityAsync(
            user.EntraId,
            [user.Email],
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(filialId, result.FilialId);
        Assert.Equal(UserKinds.InternalTest, result.UserKind);
    }

    private static UserDataRow CreateUser(
        Guid organizationId,
        Guid filialId,
        string role,
        string userKind,
        string email) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        FilialId = filialId,
        Email = email,
        DisplayName = email,
        Phone = string.Empty,
        EntraEmail = email,
        EntraId = $"entra-{Guid.NewGuid():N}",
        Role = role,
        UserKind = userKind,
        CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
    };

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

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
                    UserKind TEXT NOT NULL DEFAULT 'Member',
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
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
