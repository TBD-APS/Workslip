using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Auth;

public sealed class AuthServiceEntraLoginTests
{
    [Fact]
    public async Task CompleteEntraLoginAsync_WhenMappedTenantUserExists_ReturnsAuthUser()
    {
        var organizationId = Guid.NewGuid();
        var user = CreateUser(Roles.User, organizationId);
        var service = CreateService(
            new FakeCurrentUserContext(user.Id, organizationId, Roles.User),
            new FakeUserRepository(user));

        var result = await service.CompleteEntraLoginAsync(CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(user.Id, result.Value.UserId);
        Assert.Equal(user.OrganizationId, result.Value.OrganizationId);
        Assert.Equal(user.Email, result.Value.Email);
        Assert.Equal(user.DisplayName, result.Value.DisplayName);
        Assert.Equal(user.Role, result.Value.Role);
    }

    [Fact]
    public async Task CompleteEntraLoginAsync_WhenMappedPlatformSuperadminExists_ReturnsAuthUserWithoutOrganization()
    {
        var user = CreateUser(Roles.Superadmin, organizationId: null);
        var service = CreateService(
            new FakeCurrentUserContext(user.Id, null, Roles.Superadmin),
            new FakeUserRepository(user));

        var result = await service.CompleteEntraLoginAsync(CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(user.Id, result.Value.UserId);
        Assert.Null(result.Value.OrganizationId);
        Assert.Equal(Roles.Superadmin, result.Value.Role);
    }

    [Fact]
    public async Task CompleteEntraLoginAsync_WhenNonSuperadminHasNoOrganization_ReturnsUnauthorized()
    {
        var user = CreateUser(Roles.Admin, organizationId: null);
        var service = CreateService(
            new FakeCurrentUserContext(user.Id, null, Roles.Admin),
            new FakeUserRepository(user));

        var result = await service.CompleteEntraLoginAsync(CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task CompleteEntraLoginAsync_WhenClaimsAreNotMapped_ReturnsUnauthorized()
    {
        var service = CreateService(
            new FakeCurrentUserContext(null, null, null),
            new FakeUserRepository(null));

        var result = await service.CompleteEntraLoginAsync(CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task CompleteEntraLoginAsync_WhenMappedUserMissing_ReturnsUnauthorized()
    {
        var service = CreateService(
            new FakeCurrentUserContext(Guid.NewGuid(), Guid.NewGuid(), Roles.User),
            new FakeUserRepository(null));

        var result = await service.CompleteEntraLoginAsync(CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    private static UserDataRow CreateUser(string role, Guid? organizationId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Email = "jane@example.test",
        DisplayName = "Jane",
        EntraEmail = "jane@example.test",
        EntraId = "entra-1",
        Role = role,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static AuthService CreateService(
        FakeCurrentUserContext currentUser,
        FakeUserRepository users) =>
        new(
            currentUser,
            users,
            new FakeEmailService(),
            new InlineValidator<UpdateUserRequest>(),
            NullLogger<AuthService>.Instance);

    private sealed class FakeCurrentUserContext(
        Guid? userId,
        Guid? organizationId,
        string? role) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role { get; } = role;
    }

    private sealed class FakeUserRepository(UserDataRow? user) : IUserRepository
    {
        public Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(user?.Id == id ? user : null);

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
            Task.FromResult<IReadOnlyList<UserDataRow>>(Array.Empty<UserDataRow>());

        public Task<int> GetCountByOrganizationIdAsync(
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id);

        public Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignedJobResponse>>(Array.Empty<AssignedJobResponse>());

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

    private sealed class FakeEmailService : IEmailService
    {
        public Task SendInviteEmailAsync(
            string toEmail,
            string token,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SendOtcEmailAsync(
            string toEmail,
            string code,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
