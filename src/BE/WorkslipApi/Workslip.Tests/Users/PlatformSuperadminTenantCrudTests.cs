using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Users;

public sealed class PlatformSuperadminTenantCrudTests
{
    [Fact]
    public async Task UpdateAsync_WhenTargetIsPlatformSuperadmin_ReturnsConflictWithoutPersisting()
    {
        var organizationId = Guid.NewGuid();
        var superadmin = CreatePlatformSuperadmin();
        var repository = new FakeUserRepository(superadmin);
        var service = CreateService(repository, superadmin.Id, organizationId);

        var result = await service.UpdateAsync(
            superadmin.Id,
            new UpdateUserRequest("Changed", null, null),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("superadmin_role_protected", result.Errors);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public async Task DeleteAsync_WhenTargetIsPlatformSuperadmin_ReturnsConflictWithoutDeleting()
    {
        var organizationId = Guid.NewGuid();
        var superadmin = CreatePlatformSuperadmin();
        var repository = new FakeUserRepository(superadmin);
        var service = CreateService(repository, superadmin.Id, organizationId);

        var result = await service.DeleteAsync(superadmin.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("superadmin_role_protected", result.Errors);
        Assert.Equal(0, repository.DeleteCalls);
    }

    private static UserService CreateService(
        FakeUserRepository repository,
        Guid userId,
        Guid organizationId) =>
        new(
            repository,
            new InlineValidator<CreateUserRequest>(),
            new InlineValidator<UpdateUserRequest>(),
            new FakeUserEntraService(),
            new FakeCurrentUserContext(userId, organizationId, Roles.Superadmin),
            NullLogger<UserService>.Instance);

    private static UserDataRow CreatePlatformSuperadmin() => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = null,
        Email = "platform@example.test",
        DisplayName = "Platform Admin",
        EntraId = "entra-platform",
        EntraEmail = "platform_example.test#EXT#@tenant.onmicrosoft.com",
        Phone = string.Empty,
        Role = Roles.Superadmin,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class FakeCurrentUserContext(
        Guid? userId,
        Guid? organizationId,
        string? role) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role { get; } = role;
    }

    private sealed class FakeUserRepository(UserDataRow user) : IUserRepository
    {
        public int UpdateCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(id == user.Id ? user : null);

        public Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(null);

        public Task<UserDataRow?> GetByExternalIdentityAsync(
            string? entraId,
            IReadOnlyCollection<string> emailCandidates,
            CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(null);

        public Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(
            Guid organizationId,
            int limit,
            int offset,
            string? search,
            string? sortBy,
            string? sortDirection,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserDataRow>>([]);

        public Task<int> GetCountByOrganizationIdAsync(
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<Guid> CreateAsync(UserDataRow createdUser, CancellationToken cancellationToken) =>
            Task.FromResult(createdUser.Id);

        public Task UpdateAsync(UserDataRow updatedUser, CancellationToken cancellationToken)
        {
            UpdateCalls++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignedJobResponse>>([]);

        public Task<decimal?> GetTotalHoursAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<decimal?>(0);

        public Task<IReadOnlyDictionary<Guid, UserPeriodHours>> GetPeriodHoursAsync(
            Guid organizationId,
            DateOnly biweeklyStart,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, UserPeriodHours>>(
                new Dictionary<Guid, UserPeriodHours>());
    }

    private sealed class FakeUserEntraService : IUserEntraService
    {
        public Task<CreateEntraUserResult> CreateUserAsync(
            string email,
            string displayName,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(
            string email,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CreateEntraUserResult> InviteAdminAsync(
            string email,
            string displayName,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
