using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Organizations;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Organizations;

public sealed class OrganizationSessionServiceTests
{
    [Fact]
    public async Task CreateAsync_WhenSuperadminSelectsOrganization_ReturnsDelegatedContext()
    {
        var homeOrganizationId = Guid.NewGuid();
        var targetOrganization = CreateOrganization(Guid.NewGuid());
        var actor = CreateUser(homeOrganizationId, Roles.Superadmin);
        var users = new FakeUserRepository(actor);
        var service = new OrganizationSessionService(
            new FakeCurrentUserContext(actor.Id, homeOrganizationId, Roles.Superadmin),
            users,
            new FakeOrganizationAdministrationRepository(targetOrganization),
            NullLogger<OrganizationSessionService>.Instance);

        var result = await service.CreateAsync(targetOrganization.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(1, users.GetAuthenticatedActorCalls);
        Assert.Equal(0, users.GetByIdCalls);
        Assert.Equal(actor.Id, result.Value.User.UserId);
        Assert.Equal(targetOrganization.Id, result.Value.User.OrganizationId);
        Assert.Equal(homeOrganizationId, result.Value.HomeOrganizationId);
        Assert.Equal(Roles.Superadmin, result.Value.User.Role);
        Assert.Equal(targetOrganization.Name, result.Value.Organization.Name);
    }

    [Fact]
    public async Task CreateAsync_WhenTargetOrganizationDoesNotExist_ReturnsNotFound()
    {
        var homeOrganizationId = Guid.NewGuid();
        var actor = CreateUser(homeOrganizationId, Roles.Superadmin);
        var service = CreateService(
            new FakeCurrentUserContext(actor.Id, homeOrganizationId, Roles.Superadmin),
            actor,
            organization: null);

        var result = await service.CreateAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task CreateAsync_WhenClaimRoleIsNotSuperadmin_ReturnsForbiddenWithoutRepositoryAccess()
    {
        var context = new FakeCurrentUserContext(Guid.NewGuid(), Guid.NewGuid(), Roles.Admin);
        var users = new FakeUserRepository(null);
        var organizations = new FakeOrganizationAdministrationRepository(null);
        var service = new OrganizationSessionService(
            context,
            users,
            organizations,
            NullLogger<OrganizationSessionService>.Instance);

        var result = await service.CreateAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(0, users.GetAuthenticatedActorCalls);
        Assert.Equal(0, users.GetByIdCalls);
        Assert.Equal(0, organizations.GetOrganizationCalls);
    }

    [Fact]
    public async Task CreateAsync_WhenDatabaseRoleWasRevoked_ReturnsForbidden()
    {
        var homeOrganizationId = Guid.NewGuid();
        var actor = CreateUser(homeOrganizationId, Roles.Admin);
        var organizations = new FakeOrganizationAdministrationRepository(CreateOrganization(Guid.NewGuid()));
        var users = new FakeUserRepository(actor);
        var service = new OrganizationSessionService(
            new FakeCurrentUserContext(actor.Id, homeOrganizationId, Roles.Superadmin),
            users,
            organizations,
            NullLogger<OrganizationSessionService>.Instance);

        var result = await service.CreateAsync(organizations.Organization!.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(1, users.GetAuthenticatedActorCalls);
        Assert.Equal(0, users.GetByIdCalls);
        Assert.Equal(0, organizations.GetOrganizationCalls);
    }

    private static OrganizationSessionService CreateService(
        ICurrentUserContext currentUser,
        UserDataRow actor,
        OrganizationRow? organization) =>
        new(
            currentUser,
            new FakeUserRepository(actor),
            new FakeOrganizationAdministrationRepository(organization),
            NullLogger<OrganizationSessionService>.Instance);

    private static UserDataRow CreateUser(Guid organizationId, string role) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Email = "superadmin@example.test",
        DisplayName = "Platform operator",
        Phone = string.Empty,
        EntraEmail = "superadmin@example.test",
        EntraId = "entra-superadmin",
        Role = role,
        CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
    };

    private static OrganizationRow CreateOrganization(Guid id) => new()
    {
        Id = id,
        Name = "Target organization",
        Cvr = "12345678",
        CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
    };

    private sealed record FakeCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class FakeOrganizationAdministrationRepository(OrganizationRow? organization)
        : IOrganizationAdministrationRepository
    {
        public OrganizationRow? Organization { get; } = organization;
        public int GetOrganizationCalls { get; private set; }

        public Task<OrganizationRow?> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
        {
            GetOrganizationCalls++;
            return Task.FromResult(Organization?.Id == organizationId ? Organization : null);
        }

        public Task<UserDataRow?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(null);

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
            Task.FromResult<OrganizationUserRow?>(null);

        public Task<Guid?> CreateUserAsync(UserDataRow user, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);

        public Task<bool> UpdateUserAsync(
            UserDataRow user,
            string expectedEmail,
            string expectedEntraId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> DeleteUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class FakeUserRepository(UserDataRow? user) : IUserRepository
    {
        public int GetAuthenticatedActorCalls { get; private set; }
        public int GetByIdCalls { get; private set; }

        public Task<UserDataRow?> GetAuthenticatedActorAsync(Guid id, CancellationToken cancellationToken)
        {
            GetAuthenticatedActorCalls++;
            return Task.FromResult(user?.Id == id ? user : null);
        }

        public Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            GetByIdCalls++;
            return Task.FromResult(user?.Id == id ? user : null);
        }

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

        public Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<Guid> CreateAsync(UserDataRow created, CancellationToken cancellationToken) =>
            Task.FromResult(created.Id);

        public Task UpdateAsync(UserDataRow updated, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignedJobResponse>>([]);

        public Task<decimal?> GetTotalHoursAsync(
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<decimal?>(null);

        public Task<IReadOnlyDictionary<Guid, UserPeriodHours>> GetPeriodHoursAsync(
            Guid organizationId,
            DateOnly biweeklyStart,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, UserPeriodHours>>(
                new Dictionary<Guid, UserPeriodHours>());
    }
}
