using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Application.Users.Validators;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Tests.TestDoubles;
using Xunit;

namespace Workslip.Tests.Users;

public sealed class UserServiceAuthorizationTests
{
    [Fact]
    public async Task CreateAsync_AdminCannotCreateSuperadmin()
    {
        var repository = new FakeUserRepository();
        var entra = new FakeEntraService();
        var service = CreateService(Roles.Admin, repository, entra);

        var result = await service.CreateAsync(
            new CreateUserRequest("synthetic@example.test", "Synthetic", "+4512345678", Roles.Superadmin),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(0, repository.CreateCalls);
        Assert.Equal(0, entra.CreateCalls);
    }

    [Fact]
    public async Task UpdateAsync_AdminCannotPromoteUserToSuperadmin()
    {
        var target = CreateUser(Roles.User);
        var repository = new FakeUserRepository { ExistingById = target };
        var service = CreateService(Roles.Admin, repository, new FakeEntraService());

        var result = await service.UpdateAsync(
            target.Id,
            new UpdateUserRequest(null, null, Roles.Superadmin),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(Roles.User, target.Role);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public async Task UpdateAsync_AdminCannotModifyExistingSuperadmin()
    {
        var target = CreateUser(Roles.Superadmin);
        var repository = new FakeUserRepository { ExistingById = target };
        var service = CreateService(Roles.Admin, repository, new FakeEntraService());

        var result = await service.UpdateAsync(
            target.Id,
            new UpdateUserRequest("Changed", null, null),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal("Target", target.DisplayName);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public async Task DeleteAsync_AdminCannotDeleteExistingSuperadmin()
    {
        var target = CreateUser(Roles.Superadmin);
        var repository = new FakeUserRepository { ExistingById = target };
        var service = CreateService(Roles.Admin, repository, new FakeEntraService());

        var result = await service.DeleteAsync(target.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(0, repository.DeleteCalls);
    }

    [Fact]
    public async Task CreateAsync_SuperadminCanCreateSuperadminAndDefaultsToMemberAudience()
    {
        var repository = new FakeUserRepository();
        var entra = new FakeEntraService();
        var service = CreateService(Roles.Superadmin, repository, entra);

        var result = await service.CreateAsync(
            new CreateUserRequest("synthetic@example.test", "Synthetic", "+4512345678", Roles.Superadmin),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(1, repository.CreateCalls);
        Assert.Equal(1, entra.CreateCalls);
        Assert.Equal(Roles.Superadmin, result.Value!.Role);
        Assert.Equal(UserKinds.Member, repository.LastCreated?.UserKind);
    }

    [Fact]
    public async Task CreateAsync_MemberAdminCreatesMemberUser()
    {
        var repository = new FakeUserRepository { ActorUserKind = UserKinds.Member };
        var entra = new FakeEntraService();
        var service = CreateService(Roles.Admin, repository, entra);

        var result = await service.CreateAsync(
            new CreateUserRequest("admin@example.test", "Admin", "+4512345678", Roles.Admin),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(1, repository.CreateCalls);
        Assert.Equal(UserKinds.Member, repository.LastCreated?.UserKind);
    }

    [Fact]
    public async Task CreateAsync_InternalTestAdminCreatesInternalTestUser()
    {
        var repository = new FakeUserRepository { ActorUserKind = UserKinds.InternalTest };
        var entra = new FakeEntraService();
        var service = CreateService(Roles.Admin, repository, entra);

        var result = await service.CreateAsync(
            new CreateUserRequest("qa@example.test", "QA", "+4512345678", Roles.User),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(UserKinds.InternalTest, repository.LastCreated?.UserKind);
    }

    [Fact]
    public async Task UpdateAsync_WhenUserIsUpdated_InvalidatesAuthorizationCache()
    {
        var target = CreateUser(Roles.Admin);
        var repository = new FakeUserRepository { ExistingById = target };
        var claimsCache = new FakeClaimsCacheInvalidator();
        var service = CreateService(Roles.Admin, repository, new FakeEntraService(), claimsCache);

        var result = await service.UpdateAsync(
            target.Id,
            new UpdateUserRequest(null, null, Roles.User),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(1, claimsCache.Calls);
        Assert.Equal(target.EntraId, claimsCache.EntraId);
        Assert.Equal(target.Email, claimsCache.Email);
        Assert.Equal(target.EntraEmail, claimsCache.EntraEmail);
    }

    [Fact]
    public async Task DeleteAsync_WhenUserIsDeleted_InvalidatesAuthorizationCache()
    {
        var target = CreateUser(Roles.User);
        var repository = new FakeUserRepository { ExistingById = target };
        var claimsCache = new FakeClaimsCacheInvalidator();
        var service = CreateService(Roles.Admin, repository, new FakeEntraService(), claimsCache);

        var result = await service.DeleteAsync(target.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.NoContent, result.Status);
        Assert.Equal(1, claimsCache.Calls);
        Assert.Equal(target.EntraId, claimsCache.EntraId);
        Assert.Equal(target.Email, claimsCache.Email);
        Assert.Equal(target.EntraEmail, claimsCache.EntraEmail);
    }

    private static UserService CreateService(
        string actorRole,
        FakeUserRepository repository,
        FakeEntraService entra,
        FakeClaimsCacheInvalidator? claimsCache = null) =>
        new(
            repository,
            new CreateUserRequestValidator(),
            new UpdateUserRequestValidator(),
            entra,
            claimsCache ?? new FakeClaimsCacheInvalidator(),
            new FakeCurrentUserContext(actorRole),
            new NoOpImageStorage(),
            NullLogger<UserService>.Instance);

    private static UserDataRow CreateUser(string role) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = FakeCurrentUserContext.Organization,
            Email = "target@example.test",
            DisplayName = "Target",
            Phone = "+4512345678",
            Role = role,
            UserKind = UserKinds.Member,
            EntraId = "entra-target",
            EntraEmail = "target@example.test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private sealed class FakeCurrentUserContext(string role) : ICurrentUserContext
    {
        public static readonly Guid Organization = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid Actor = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid? UserId { get; } = Actor;
        public Guid? OrganizationId { get; } = Organization;
        public string? Role { get; } = role;
    }

    private sealed class FakeEntraService : IUserEntraService
    {
        public int CreateCalls { get; private set; }

        public Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, CancellationToken ct)
        {
            CreateCalls++;
            return Task.FromResult(new CreateEntraUserResult("entra-new", email, displayName, Created: true));
        }

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(string email, CancellationToken ct) =>
            Task.FromResult(new CreateEntraUserResult("entra-existing", email, email, Created: false));

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeClaimsCacheInvalidator : IUserClaimsCacheInvalidator
    {
        public int Calls { get; private set; }
        public string? EntraId { get; private set; }
        public string? Email { get; private set; }
        public string? EntraEmail { get; private set; }

        public void Invalidate(string? entraId, string? email, string? entraEmail)
        {
            Calls++;
            EntraId = entraId;
            Email = email;
            EntraEmail = entraEmail;
        }
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public UserDataRow? ExistingById { get; init; }
        public string ActorUserKind { get; init; } = UserKinds.Member;
        public UserDataRow? LastCreated { get; private set; }
        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public Task<UserDataRow?> GetAuthenticatedActorAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(id == FakeCurrentUserContext.Actor
                ? new UserDataRow
                {
                    Id = FakeCurrentUserContext.Actor,
                    OrganizationId = FakeCurrentUserContext.Organization,
                    Role = Roles.Admin,
                    UserKind = ActorUserKind
                }
                : null);

        public Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingById?.Id == id ? ExistingById : null);

        public Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(null);

        public Task<UserDataRow?> GetByExternalIdentityAsync(string? entraId, IReadOnlyCollection<string> emailCandidates, CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(null);

        public Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserDataRow>>(Array.Empty<UserDataRow>());

        public Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken)
        {
            CreateCalls++;
            LastCreated = user;
            return Task.FromResult(user.Id);
        }

        public Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken)
        {
            UpdateCalls++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignedJobResponse>>(Array.Empty<AssignedJobResponse>());

        public Task<decimal?> GetTotalHoursAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<decimal?>(0m);

        public Task<IReadOnlyDictionary<Guid, UserPeriodHours>> GetPeriodHoursAsync(Guid organizationId, DateOnly biweeklyStart, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, UserPeriodHours>>(new Dictionary<Guid, UserPeriodHours>());
    }
}
