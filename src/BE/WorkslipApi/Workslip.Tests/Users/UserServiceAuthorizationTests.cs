using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Application.Users.Validators;
using Workslip.Domain;
using Workslip.Domain.Models;
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
    public async Task CreateAsync_SuperadminCanCreateSuperadmin()
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
        Assert.Equal(UserKinds.Member, result.Value.UserKind);
    }

    [Fact]
    public async Task CreateAsync_AdminCanStillCreateAdmin()
    {
        var repository = new FakeUserRepository();
        var entra = new FakeEntraService();
        var service = CreateService(Roles.Admin, repository, entra);

        var result = await service.CreateAsync(
            new CreateUserRequest("admin@example.test", "Admin", "+4512345678", Roles.Admin),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(1, repository.CreateCalls);
        Assert.Equal(1, entra.CreateCalls);
        Assert.Equal(Roles.Admin, result.Value!.Role);
        Assert.Equal(UserKinds.Member, result.Value.UserKind);
    }

    [Fact]
    public async Task CreateAsync_InternalTestAdminCreatesInternalTestPeer()
    {
        var repository = new FakeUserRepository(UserKinds.InternalTest);
        var service = CreateService(Roles.Admin, repository, new FakeEntraService());

        var result = await service.CreateAsync(
            new CreateUserRequest("test-peer@example.test", "Test peer", "+4512345678", Roles.User),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(UserKinds.InternalTest, result.Value!.UserKind);
    }

    [Fact]
    public async Task GetDetailAsync_MemberAdminCannotDiscoverInternalTestUser()
    {
        var target = CreateUser(Roles.User, UserKinds.InternalTest);
        var repository = new FakeUserRepository { ExistingById = target };
        var service = CreateService(Roles.Admin, repository, new FakeEntraService());

        var result = await service.GetDetailAsync(target.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task UpdateAsync_MemberAdminCannotModifyInternalTestUser()
    {
        var target = CreateUser(Roles.User, UserKinds.InternalTest);
        var repository = new FakeUserRepository { ExistingById = target };
        var service = CreateService(Roles.Admin, repository, new FakeEntraService());

        var result = await service.UpdateAsync(
            target.Id,
            new UpdateUserRequest("Changed", null, null),
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal("Target", target.DisplayName);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public async Task UpdateAsync_InternalTestAdminCanModifyInternalTestUser()
    {
        var target = CreateUser(Roles.User, UserKinds.InternalTest);
        var repository = new FakeUserRepository(UserKinds.InternalTest) { ExistingById = target };
        var service = CreateService(Roles.Admin, repository, new FakeEntraService());

        var result = await service.UpdateAsync(
            target.Id,
            new UpdateUserRequest("Changed", null, null),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal("Changed", target.DisplayName);
        Assert.Equal(1, repository.UpdateCalls);
    }

    [Fact]
    public async Task SetUserKindAsync_AdminCannotClassifyTestIdentity()
    {
        var target = CreateUser(Roles.User);
        var repository = new FakeUserRepository { ExistingById = target };
        var service = CreateService(Roles.Admin, repository, new FakeEntraService());

        var result = await service.SetUserKindAsync(
            target.Id,
            new SetUserKindRequest(UserKinds.InternalTest),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(UserKinds.Member, target.UserKind);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public async Task SetUserKindAsync_SuperadminCanClassifyTestIdentity()
    {
        var target = CreateUser(Roles.User);
        var repository = new FakeUserRepository { ExistingById = target };
        var service = CreateService(Roles.Superadmin, repository, new FakeEntraService());

        var result = await service.SetUserKindAsync(
            target.Id,
            new SetUserKindRequest(UserKinds.InternalTest),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(UserKinds.InternalTest, target.UserKind);
        Assert.Equal(UserKinds.InternalTest, result.Value!.UserKind);
        Assert.Equal(1, repository.UpdateCalls);
    }

    [Fact]
    public async Task SetUserKindAsync_RejectsUnknownKind()
    {
        var target = CreateUser(Roles.User);
        var repository = new FakeUserRepository { ExistingById = target };
        var service = CreateService(Roles.Superadmin, repository, new FakeEntraService());

        var result = await service.SetUserKindAsync(
            target.Id,
            new SetUserKindRequest("Hidden"),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(UserKinds.Member, target.UserKind);
        Assert.Equal(0, repository.UpdateCalls);
    }

    private static UserService CreateService(
        string actorRole,
        FakeUserRepository repository,
        FakeEntraService entra)
    {
        repository.AuthenticatedActor.Role = actorRole;
        return new UserService(
            repository,
            new CreateUserRequestValidator(),
            new UpdateUserRequestValidator(),
            entra,
            new FakeCurrentUserContext(actorRole),
            NullLogger<UserService>.Instance);
    }

    private static UserDataRow CreateUser(string role, string userKind = UserKinds.Member) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = FakeCurrentUserContext.Organization,
            FilialId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Email = "target@example.test",
            DisplayName = "Target",
            Phone = "+4512345678",
            Role = role,
            UserKind = userKind,
            EntraId = "entra-target",
            EntraEmail = "target@example.test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private sealed class FakeCurrentUserContext(string role) : ICurrentUserContext
    {
        public static readonly Guid Organization = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public static readonly Guid ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid? UserId { get; } = ActorId;
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

    private sealed class FakeUserRepository : IUserRepository
    {
        public FakeUserRepository(string actorUserKind = UserKinds.Member)
        {
            AuthenticatedActor = CreateUser(Roles.Admin, actorUserKind);
            AuthenticatedActor.Id = FakeCurrentUserContext.ActorId;
        }

        public UserDataRow AuthenticatedActor { get; }
        public UserDataRow? ExistingById { get; init; }
        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public Task<UserDataRow?> GetAuthenticatedActorAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(AuthenticatedActor.Id == id ? AuthenticatedActor : null);

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
