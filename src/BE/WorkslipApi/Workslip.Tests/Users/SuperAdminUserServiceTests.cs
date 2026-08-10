using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Application.Users.Validators;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Users;

public sealed class SuperAdminUserServiceTests
{
    [Fact]
    public async Task ListAsync_WhenActorIsNotSuperadmin_ReturnsForbiddenWithoutQueryingUsers()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository, role: Roles.Admin);

        var result = await service.ListAsync(null, null, null, null, null, CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(0, repository.ListCalls);
    }

    [Fact]
    public async Task CreateAsync_WhenRoleIsSuperadmin_ReturnsInvalidWithoutCreatingIdentity()
    {
        var repository = new FakeRepository { TenantFilialExists = true };
        var entra = new FakeEntraService();
        var service = CreateService(repository, entra: entra);

        var result = await service.CreateAsync(
            new SuperAdminCreateUserRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "user@example.test",
                "User",
                string.Empty,
                Roles.Superadmin),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(0, entra.CreateCalls);
        Assert.Equal(0, repository.CreateCalls);
    }

    [Fact]
    public async Task CreateAsync_WhenFilialDoesNotBelongToOrganization_ReturnsInvalidWithoutCreatingIdentity()
    {
        var repository = new FakeRepository { TenantFilialExists = false };
        var entra = new FakeEntraService();
        var service = CreateService(repository, entra: entra);

        var result = await service.CreateAsync(
            new SuperAdminCreateUserRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "user@example.test",
                "User",
                string.Empty,
                Roles.User),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(0, entra.CreateCalls);
        Assert.Equal(0, repository.CreateCalls);
    }

    [Fact]
    public async Task UpdateAsync_WhenFilialDoesNotBelongToUsersOrganization_ReturnsInvalidWithoutUpdate()
    {
        var repository = new FakeRepository
        {
            User = CreateRecord(),
            TenantFilialExists = false
        };
        var service = CreateService(repository);

        var result = await service.UpdateAsync(
            repository.User.Id,
            new SuperAdminUpdateUserRequest(null, null, Roles.Admin, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public async Task UpdateAsync_WhenRoleChanges_UpdatesTenantUserAndInvalidatesAuthorizationCache()
    {
        var user = CreateRecord();
        var repository = new FakeRepository
        {
            User = user,
            TenantFilialExists = true,
            EmailUser = new UserDataRow
            {
                Id = user.Id,
                OrganizationId = user.OrganizationId,
                FilialId = user.FilialId,
                Email = user.Email,
                EntraId = "entra-id",
                EntraEmail = "entra@example.test",
                DisplayName = user.DisplayName,
                Phone = user.Phone,
                Role = user.Role
            }
        };
        var cache = new FakeClaimsCacheInvalidator();
        var service = CreateService(repository, claimsCache: cache);

        var result = await service.UpdateAsync(
            user.Id,
            new SuperAdminUpdateUserRequest("Updated user", "12345678", Roles.Admin, user.FilialId),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(1, repository.UpdateCalls);
        Assert.Equal(Roles.Admin, repository.LastUpdatedRole);
        Assert.Equal(1, cache.Calls);
        Assert.Equal("entra-id", cache.EntraId);
        Assert.Equal(user.Email, cache.Email);
    }

    [Fact]
    public async Task DeleteAsync_WhenUserHasHistory_ReturnsConflictAndDoesNotInvalidateAuthorizationCache()
    {
        var user = CreateRecord();
        var repository = new FakeRepository
        {
            User = user,
            EmailUser = new UserDataRow
            {
                Id = user.Id,
                OrganizationId = user.OrganizationId,
                FilialId = user.FilialId,
                Email = user.Email,
                EntraId = "entra-id",
                EntraEmail = user.Email,
                Role = user.Role
            },
            DeleteStatus = SuperAdminUserDeleteStatus.HasHistory
        };
        var cache = new FakeClaimsCacheInvalidator();
        var service = CreateService(repository, claimsCache: cache);

        var result = await service.DeleteAsync(user.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("user_has_history", result.Errors);
        Assert.Equal(0, cache.Calls);
    }

    private static SuperAdminUserService CreateService(
        FakeRepository repository,
        string role = Roles.Superadmin,
        FakeEntraService? entra = null,
        FakeClaimsCacheInvalidator? claimsCache = null) =>
        new(
            repository,
            new CreateUserRequestValidator(),
            new UpdateUserRequestValidator(),
            entra ?? new FakeEntraService(),
            claimsCache ?? new FakeClaimsCacheInvalidator(),
            new TestCurrentUserContext(role),
            NullLogger<SuperAdminUserService>.Instance);

    private static SuperAdminUserRecord CreateRecord() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Organization A",
            Guid.NewGuid(),
            "Hovedfilial",
            "user@example.test",
            "Original user",
            string.Empty,
            Roles.User,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow);

    private sealed class TestCurrentUserContext(string role) : ICurrentUserContext
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? OrganizationId => PlatformOrganization.Id;
        public string? Role => role;
    }

    private sealed class FakeClaimsCacheInvalidator : IUserClaimsCacheInvalidator
    {
        public int Calls { get; private set; }
        public string? EntraId { get; private set; }
        public string? Email { get; private set; }

        public void Invalidate(string? entraId, string? email, string? entraEmail)
        {
            Calls++;
            EntraId = entraId;
            Email = email;
        }
    }

    private sealed class FakeEntraService : IUserEntraService
    {
        public int CreateCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, CancellationToken ct)
        {
            CreateCalls++;
            return Task.FromResult(new CreateEntraUserResult("entra-id", email, displayName, Created: true));
        }

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(string email, CancellationToken ct) =>
            CreateUserAsync(email, email, ct);

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepository : ISuperAdminUserRepository
    {
        public int ListCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public string? LastUpdatedRole { get; private set; }
        public bool TenantFilialExists { get; set; } = true;
        public SuperAdminUserRecord? User { get; set; }
        public UserDataRow? EmailUser { get; set; }
        public SuperAdminUserDeleteStatus DeleteStatus { get; set; } = SuperAdminUserDeleteStatus.Deleted;

        public Task<IReadOnlyList<SuperAdminUserRecord>> ListAsync(
            int limit,
            int offset,
            string? search,
            string? sortBy,
            string? sortDirection,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            IReadOnlyList<SuperAdminUserRecord> rows = User is null ? [] : [User];
            return Task.FromResult(rows);
        }

        public Task<int> CountAsync(string? search, CancellationToken cancellationToken) =>
            Task.FromResult(User is null ? 0 : 1);

        public Task<SuperAdminUserRecord?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(User?.Id == userId ? User : null);

        public Task<IReadOnlyList<SuperAdminFilialRecord>> ListFilialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SuperAdminFilialRecord>>([]);

        public Task<bool> TenantFilialExistsAsync(
            Guid organizationId,
            Guid filialId,
            CancellationToken cancellationToken) =>
            Task.FromResult(TenantFilialExists);

        public Task<UserDataRow?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(EmailUser);

        public Task<Guid?> CreateAsync(UserDataRow user, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult<Guid?>(user.Id);
        }

        public Task<bool> UpdateAsync(
            Guid userId,
            string displayName,
            string phone,
            string role,
            Guid filialId,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken)
        {
            UpdateCalls++;
            LastUpdatedRole = role;
            if (User is not null && User.Id == userId)
            {
                User = User with
                {
                    DisplayName = displayName,
                    Phone = phone,
                    Role = role,
                    FilialId = filialId,
                    UpdatedAt = updatedAt
                };
            }
            return Task.FromResult(true);
        }

        public Task<SuperAdminUserDeleteStatus> DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(DeleteStatus);

        public Task<bool> IsEntraIdentityReferencedAsync(string entraUserId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
