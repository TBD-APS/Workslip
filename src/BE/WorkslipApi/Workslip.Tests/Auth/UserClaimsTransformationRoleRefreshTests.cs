using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Api;
using Workslip.Api.Helpers;
using Workslip.Application.Common;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Auth;

public sealed class UserClaimsTransformationRoleRefreshTests
{
    [Fact]
    public async Task TransformAsync_LocalSession_RefreshesRoleAndOrganizationFromWorkslipDatabase()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new FakeUserRepository
        {
            ExternalUser = new UserDataRow
            {
                Id = userId,
                OrganizationId = organizationId,
                FilialId = Guid.NewGuid(),
                Email = "user@example.test",
                EntraEmail = "user@example.test",
                EntraId = "entra-user",
                DisplayName = "User",
                Phone = string.Empty,
                Role = Roles.Admin
            }
        };
        var transformation = CreateTransformation(repository);
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, "user@example.test"),
                new Claim("organizationId", Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, Roles.User)
            ],
            authenticationType: "LocalJwt");
        var principal = new ClaimsPrincipal(identity);

        var transformed = await transformation.TransformAsync(principal);

        Assert.Equal(1, repository.ExternalIdentityCalls);
        Assert.Equal(organizationId.ToString(), transformed.FindFirstValue("organizationId"));
        Assert.Equal(userId.ToString(), transformed.FindFirstValue("workslipUserId"));
        Assert.Equal(Roles.Admin, transformed.FindFirstValue(ClaimTypes.Role));
        Assert.DoesNotContain(transformed.Claims, claim => claim.Type == ClaimTypes.Role && claim.Value == Roles.User);
    }

    [Fact]
    public async Task TransformAsync_DelegatedSuperadminSession_PreservesEffectiveTenantClaimsWithoutDatabaseLookup()
    {
        var effectiveOrganizationId = Guid.NewGuid();
        var repository = new FakeUserRepository();
        var transformation = CreateTransformation(repository);
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Email, "superadmin@example.test"),
                new Claim("organizationId", effectiveOrganizationId.ToString()),
                new Claim(ClaimTypes.Role, Roles.Superadmin),
                new Claim(JwtHelper.DelegatedOrganizationSessionClaim, bool.TrueString.ToLowerInvariant())
            ],
            authenticationType: "LocalJwt");
        var principal = new ClaimsPrincipal(identity);

        var transformed = await transformation.TransformAsync(principal);

        Assert.Equal(0, repository.ExternalIdentityCalls);
        Assert.Equal(effectiveOrganizationId.ToString(), transformed.FindFirstValue("organizationId"));
        Assert.Equal(Roles.Superadmin, transformed.FindFirstValue(ClaimTypes.Role));
    }

    private static UserClaimsTransformation CreateTransformation(IUserRepository repository)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var diagnostics = new CacheDiagnostics([
            new CacheRegionDefinition(CacheRegionNames.AuthenticatedUsers, "Memory", 3600)
        ]);
        return new UserClaimsTransformation(
            repository,
            cache,
            diagnostics,
            NullLogger<UserClaimsTransformation>.Instance);
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public UserDataRow? ExternalUser { get; init; }
        public int ExternalIdentityCalls { get; private set; }

        public Task<UserDataRow?> GetByExternalIdentityAsync(
            string? entraId,
            IReadOnlyCollection<string> emailCandidates,
            CancellationToken cancellationToken)
        {
            ExternalIdentityCalls++;
            return Task.FromResult(ExternalUser);
        }

        public Task<UserDataRow?> GetAuthenticatedActorAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<decimal?> GetTotalHoursAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, UserPeriodHours>> GetPeriodHoursAsync(Guid organizationId, DateOnly biweeklyStart, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
