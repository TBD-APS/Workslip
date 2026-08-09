using Ardalis.Result;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Auth;

public sealed class AuthServiceEntraLoginTests
{
    [Fact]
    public async Task CompleteEntraLoginAsync_WhenMappedUserExists_ReturnsAuthUser()
    {
        var organizationId = Guid.NewGuid();
        var user = CreateUser(organizationId, Roles.User);
        var service = CreateService(new FakeCurrentUserContext(user.Id, organizationId), new FakeUserRepository(user));

        var result = await service.CompleteEntraLoginAsync(CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(user.Id, result.Value.UserId);
        Assert.Equal(user.OrganizationId, result.Value.OrganizationId);
        Assert.Equal(user.Email, result.Value.Email);
        Assert.Equal(user.DisplayName, result.Value.DisplayName);
        Assert.Equal(user.Role, result.Value.Role);
    }

    [Fact]
    public async Task GetCurrentUserAsync_InDelegatedSession_ReportsEffectiveOrganization()
    {
        var homeOrganizationId = Guid.NewGuid();
        var effectiveOrganizationId = Guid.NewGuid();
        var user = CreateUser(homeOrganizationId, Roles.Superadmin);
        var service = CreateService(
            new FakeCurrentUserContext(user.Id, effectiveOrganizationId, Roles.Superadmin),
            new FakeUserRepository(user));

        var result = await service.GetCurrentUserAsync(CancellationToken.None);

        Assert.Equal(user.Id, result.Id);
        Assert.Equal(effectiveOrganizationId, result.OrganizationId);
        Assert.Equal(Roles.Superadmin, result.Role);
    }

    [Fact]
    public async Task GetCurrentUserAsync_InDelegatedSession_UsesEfActorLookupAcrossOrganizations()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var homeOrganization = CreateOrganization("Workslip Platform", "10000001");
        var effectiveOrganization = CreateOrganization("NP Teknik", "12345678");
        var actor = CreateUser(homeOrganization.Id, Roles.Superadmin);
        database.Context.Organizations.AddRange(homeOrganization, effectiveOrganization);
        database.Context.Users.Add(actor);
        await database.Context.SaveChangesAsync();

        var currentUser = new FakeCurrentUserContext(
            actor.Id,
            effectiveOrganization.Id,
            Roles.Superadmin);
        var repository = new EfUserRepository(database.Context, currentUser);
        var service = CreateService(currentUser, repository);

        var result = await service.GetCurrentUserAsync(CancellationToken.None);

        Assert.Equal(actor.Id, result.Id);
        Assert.Equal(effectiveOrganization.Id, result.OrganizationId);
        Assert.Equal(actor.Email, result.Email);
        Assert.Equal(actor.DisplayName, result.DisplayName);
        Assert.Equal(actor.Role, result.Role);
    }

    [Fact]
    public async Task GetAuthenticatedActorAsync_WhenRequestedIdDiffersFromSignedActor_ReturnsNull()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var homeOrganization = CreateOrganization("Workslip Platform", "10000001");
        var effectiveOrganization = CreateOrganization("NP Teknik", "12345678");
        var actor = CreateUser(homeOrganization.Id, Roles.Superadmin);
        var tenantUser = CreateUser(effectiveOrganization.Id, Roles.User);
        database.Context.Organizations.AddRange(homeOrganization, effectiveOrganization);
        database.Context.Users.AddRange(actor, tenantUser);
        await database.Context.SaveChangesAsync();

        var currentUser = new FakeCurrentUserContext(
            actor.Id,
            effectiveOrganization.Id,
            Roles.Superadmin);
        var repository = new EfUserRepository(database.Context, currentUser);

        var result = await repository.GetAuthenticatedActorAsync(
            tenantUser.Id,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task DelegatedMutationPaths_DoNotUseAuthenticatedActorLookup()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var homeOrganization = CreateOrganization("Workslip Platform", "10000001");
        var effectiveOrganization = CreateOrganization("NP Teknik", "12345678");
        var actor = CreateUser(homeOrganization.Id, Roles.Superadmin);
        database.Context.Organizations.AddRange(homeOrganization, effectiveOrganization);
        database.Context.Users.Add(actor);
        await database.Context.SaveChangesAsync();

        var currentUser = new FakeCurrentUserContext(
            actor.Id,
            effectiveOrganization.Id,
            Roles.Superadmin);
        var repository = new EfUserRepository(database.Context, currentUser);
        var authService = CreateService(currentUser, repository);
        var userService = new UserService(
            repository,
            new InlineValidator<CreateUserRequest>(),
            new InlineValidator<UpdateUserRequest>(),
            new FakeUserEntraService(),
            currentUser,
            NullLogger<UserService>.Instance);

        var profileUpdate = await authService.UpdateCurrentUserAsync(
            new UpdateUserRequest("Changed through profile", null, null),
            CancellationToken.None);
        var tenantUserUpdate = await userService.UpdateAsync(
            actor.Id,
            new UpdateUserRequest("Changed through tenant user service", null, null),
            CancellationToken.None);
        var delete = await userService.DeleteAsync(actor.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, profileUpdate.Status);
        Assert.Equal(ResultStatus.NotFound, tenantUserUpdate.Status);
        Assert.Equal(ResultStatus.NotFound, delete.Status);
        var storedActor = await database.Context.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == actor.Id);
        Assert.Equal(actor.DisplayName, storedActor.DisplayName);
    }

    [Fact]
    public async Task CompleteEntraLoginAsync_WhenClaimsAreNotMapped_ReturnsUnauthorized()
    {
        var service = CreateService(new FakeCurrentUserContext(null, null), new FakeUserRepository(null));

        var result = await service.CompleteEntraLoginAsync(CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task CompleteEntraLoginAsync_WhenMappedUserMissing_ReturnsUnauthorized()
    {
        var service = CreateService(new FakeCurrentUserContext(Guid.NewGuid(), Guid.NewGuid()), new FakeUserRepository(null));

        var result = await service.CompleteEntraLoginAsync(CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    private static UserDataRow CreateUser(Guid organizationId, string role) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Email = "jane@example.test",
        DisplayName = "Jane",
        Phone = string.Empty,
        EntraEmail = "jane@example.test",
        EntraId = "entra-1",
        Role = role,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static AuthService CreateService(FakeCurrentUserContext currentUser, IUserRepository users) =>
        new(currentUser, users, new FakeEmailService(), new InlineValidator<UpdateUserRequest>(), NullLogger<AuthService>.Instance);

    private static OrganizationRow CreateOrganization(string name, string cvr) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Cvr = cvr,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class FakeCurrentUserContext(
        Guid? userId,
        Guid? organizationId,
        string? role = Roles.User) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role { get; } = role;
    }

    private sealed class FakeUserRepository(UserDataRow? user) : IUserRepository
    {
        public Task<UserDataRow?> GetAuthenticatedActorAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(user?.Id == id ? user : null);

        public Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(user?.Id == id ? user : null);

        public Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken) => Task.FromResult<UserDataRow?>(null);
        public Task<UserDataRow?> GetByExternalIdentityAsync(string? entraId, IReadOnlyCollection<string> emailCandidates, CancellationToken cancellationToken) => Task.FromResult<UserDataRow?>(null);
        public Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UserDataRow>>(Array.Empty<UserDataRow>());
        public Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
        public Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AssignedJobResponse>>(Array.Empty<AssignedJobResponse>());
        public Task<decimal?> GetTotalHoursAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => Task.FromResult<decimal?>(0);
        public Task<IReadOnlyDictionary<Guid, UserPeriodHours>> GetPeriodHoursAsync(Guid organizationId, DateOnly biweeklyStart, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<Guid, UserPeriodHours>>(new Dictionary<Guid, UserPeriodHours>());
    }

    private sealed class FakeEmailService : IEmailService
    {
        public Task SendInviteEmailAsync(string toEmail, string token, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendOtcEmailAsync(string toEmail, string code, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeUserEntraService : IUserEntraService
    {
        public Task<CreateEntraUserResult> CreateUserAsync(
            string email,
            string displayName,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(
            string email,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteUserAsync(string entraUserId, CancellationToken cancellationToken) =>
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
            SqlDbContext? context = null;
            try
            {
                await connection.OpenAsync();

                var options = new DbContextOptionsBuilder<SqlDbContext>()
                    .UseSqlite(connection)
                    .Options;
                context = new SqlDbContext(options);

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
            catch
            {
                try
                {
                    if (context is not null)
                    {
                        await context.DisposeAsync();
                    }
                }
                finally
                {
                    await connection.DisposeAsync();
                }

                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Context.DisposeAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
