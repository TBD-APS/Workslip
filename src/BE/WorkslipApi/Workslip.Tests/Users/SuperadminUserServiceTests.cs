using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Organizations;
using Workslip.Application.Users;
using Workslip.Application.Users.Validators;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Users;

public sealed class SuperadminUserServiceTests
{
    private static readonly Guid ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrganizationAId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OrganizationBId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task CreateAsync_CreatesUserInRequestedOrganizationAndProvisionsEntra()
    {
        var organization = CreateOrganization(OrganizationAId);
        var repository = new FakeAdministrationRepository { Organization = organization };
        var entra = new FakeEntraService();
        var service = CreateService(repository, entra);

        var result = await service.CreateAsync(
            new CreateAdminUserRequest(OrganizationAId, "new.user@example.test", "New User", "+4512345678", Roles.Admin),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(OrganizationAId, result.Value!.OrganizationId);
        Assert.Equal(organization.Name, result.Value!.OrganizationName);
        Assert.Equal(Roles.Admin, result.Value!.Role);
        Assert.Equal(1, repository.CreateUserCalls);
        Assert.Equal(1, entra.CreateCalls);
    }

    [Fact]
    public async Task CreateAsync_WhenOrganizationDoesNotExist_ReturnsNotFound()
    {
        var repository = new FakeAdministrationRepository { Organization = null };
        var service = CreateService(repository, new FakeEntraService());

        var result = await service.CreateAsync(
            new CreateAdminUserRequest(OrganizationAId, "new.user@example.test", "New User", null, Roles.User),
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(0, repository.CreateUserCalls);
    }

    [Fact]
    public async Task CreateAsync_WhenEmailAlreadyInUse_ReturnsConflict()
    {
        var organization = CreateOrganization(OrganizationAId);
        var existing = CreateUser(OrganizationBId, Roles.User, email: "taken@example.test");
        var repository = new FakeAdministrationRepository { Organization = organization, EmailUser = existing };
        var service = CreateService(repository, new FakeEntraService());

        var result = await service.CreateAsync(
            new CreateAdminUserRequest(OrganizationAId, "taken@example.test", "New User", null, Roles.User),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(0, repository.CreateUserCalls);
    }

    [Fact]
    public async Task CreateAsync_CanAssignSuperadminRole()
    {
        var organization = CreateOrganization(OrganizationAId);
        var repository = new FakeAdministrationRepository { Organization = organization };
        var service = CreateService(repository, new FakeEntraService());

        var result = await service.CreateAsync(
            new CreateAdminUserRequest(OrganizationAId, "promoted@example.test", "Promoted", null, Roles.Superadmin),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(Roles.Superadmin, result.Value!.Role);
    }

    [Fact]
    public async Task UpdateAsync_CanChangeRoleAcrossOrganizations()
    {
        var target = CreateUser(OrganizationAId, Roles.User);
        var repository = new FakeAdministrationRepository { UserById = target, OrganizationName = "Kunde A/S" };
        var service = CreateService(repository, new FakeEntraService());

        var result = await service.UpdateAsync(
            target.Id,
            new UpdateAdminUserRequest(null, null, Roles.Admin),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(Roles.Admin, result.Value!.Role);
        Assert.Equal(1, repository.UpdateUserCalls);
    }

    [Fact]
    public async Task UpdateAsync_WhenActorTargetsOwnAccountRoleChange_ReturnsConflict()
    {
        var self = CreateUser(OrganizationAId, Roles.Superadmin, id: ActorId);
        var repository = new FakeAdministrationRepository { UserById = self };
        var service = CreateService(repository, new FakeEntraService());

        var result = await service.UpdateAsync(
            ActorId,
            new UpdateAdminUserRequest(null, null, Roles.Admin),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(0, repository.UpdateUserCalls);
    }

    [Fact]
    public async Task UpdateAsync_WhenActorUpdatesOwnProfileWithoutRoleChange_Succeeds()
    {
        var self = CreateUser(OrganizationAId, Roles.Superadmin, id: ActorId);
        var repository = new FakeAdministrationRepository { UserById = self, OrganizationName = "Platform" };
        var service = CreateService(repository, new FakeEntraService());

        var result = await service.UpdateAsync(
            ActorId,
            new UpdateAdminUserRequest("New name", null, null),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(1, repository.UpdateUserCalls);
    }

    [Fact]
    public async Task DeleteAsync_WhenActorTargetsOwnAccount_ReturnsConflict()
    {
        var repository = new FakeAdministrationRepository();
        var service = CreateService(repository, new FakeEntraService());

        var result = await service.DeleteAsync(ActorId, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(0, repository.DeleteUserCalls);
    }

    [Fact]
    public async Task DeleteAsync_DeletesAnotherUser()
    {
        var target = CreateUser(OrganizationAId, Roles.User);
        var repository = new FakeAdministrationRepository { UserById = target };
        var service = CreateService(repository, new FakeEntraService());

        var result = await service.DeleteAsync(target.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.NoContent, result.Status);
        Assert.Equal(1, repository.DeleteUserCalls);
    }

    private static SuperadminUserService CreateService(
        FakeAdministrationRepository repository,
        FakeEntraService entra) =>
        new(
            repository,
            new CreateAdminUserRequestValidator(),
            new UpdateAdminUserRequestValidator(),
            entra,
            new FakeCurrentUserContext(),
            NullLogger<SuperadminUserService>.Instance);

    private static OrganizationRow CreateOrganization(Guid id) => new()
    {
        Id = id,
        Name = "Kunde A/S",
        Cvr = "12345678",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static UserDataRow CreateUser(Guid organizationId, string role, Guid? id = null, string? email = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        OrganizationId = organizationId,
        Email = email ?? "target@example.test",
        DisplayName = "Target",
        Phone = "+4512345678",
        Role = role,
        EntraId = "entra-target",
        EntraEmail = email ?? "target@example.test",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid? UserId { get; } = ActorId;
        public Guid? OrganizationId { get; } = OrganizationAId;
        public string? Role { get; } = Roles.Superadmin;
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

    private sealed class FakeAdministrationRepository : IOrganizationAdministrationRepository
    {
        public OrganizationRow? Organization { get; init; }
        public UserDataRow? EmailUser { get; init; }
        public UserDataRow? UserById { get; init; }
        public string OrganizationName { get; init; } = "Kunde A/S";
        public int CreateUserCalls { get; private set; }
        public int UpdateUserCalls { get; private set; }
        public int DeleteUserCalls { get; private set; }

        public Task<OrganizationRow?> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(Organization?.Id == organizationId ? Organization : null);

        public Task<UserDataRow?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(EmailUser?.Email == normalizedEmail ? EmailUser : null);

        public Task<UserDataRow?> GetUnlinkedAdminAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(null);

        public Task<bool> IsEntraIdentityReferencedAsync(string entraUserId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<Guid?> CreateAdminAsync(UserDataRow admin, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);

        public Task<bool> UpdateAdminAsync(
            UserDataRow admin,
            string expectedEmail,
            string expectedEntraId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<OrganizationUserRow>> ListUsersAsync(
            Guid? organizationId,
            int limit,
            int offset,
            string? search,
            string? sortBy,
            string? sortDirection,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OrganizationUserRow>>([]);

        public Task<int> CountUsersAsync(Guid? organizationId, string? search, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<OrganizationUserRow?> GetUserWithOrganizationAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(UserById?.Id == userId ? new OrganizationUserRow(UserById, OrganizationName) : null);

        public Task<Guid?> CreateUserAsync(UserDataRow user, CancellationToken cancellationToken)
        {
            CreateUserCalls++;
            return Task.FromResult<Guid?>(user.Id);
        }

        public Task<bool> UpdateUserAsync(
            UserDataRow user,
            string expectedEmail,
            string expectedEntraId,
            CancellationToken cancellationToken)
        {
            UpdateUserCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            DeleteUserCalls++;
            return Task.FromResult(true);
        }
    }
}
