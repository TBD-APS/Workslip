using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Api;
using Workslip.Api.Helpers;
using Workslip.Api.Middleware;
using Workslip.Application.Auth;
using Workslip.Application.Organizations;
using Workslip.Application.Users;
using Workslip.Application.Users.Validators;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Auth;

public sealed class PlatformSuperadminScopeTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void CurrentUserContext_SuperadminUsesValidatedScopeAndIgnoresLegacyClaim()
    {
        var legacyTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var httpContext = CreateHttpContext(Roles.Superadmin, legacyTenantId);
        httpContext.Items[CurrentUserContext.ValidatedOrganizationScopeItem] = TenantId;

        var currentUser = new CurrentUserContext(new HttpContextAccessor { HttpContext = httpContext });

        Assert.Equal(TenantId, currentUser.OrganizationId);
    }

    [Fact]
    public void CurrentUserContext_SuperadminWithoutValidatedScopeHasNoOrganizationScope()
    {
        var httpContext = CreateHttpContext(Roles.Superadmin, TenantId);
        httpContext.Request.Headers[CurrentUserContext.OrganizationScopeHeader] = TenantId.ToString();
        var currentUser = new CurrentUserContext(new HttpContextAccessor { HttpContext = httpContext });

        Assert.Null(currentUser.OrganizationId);
    }

    [Fact]
    public void CurrentUserContext_TenantAdminIgnoresValidatedSuperadminScope()
    {
        var claimedTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var httpContext = CreateHttpContext(Roles.Admin, claimedTenantId);
        httpContext.Items[CurrentUserContext.ValidatedOrganizationScopeItem] = TenantId;

        var currentUser = new CurrentUserContext(new HttpContextAccessor { HttpContext = httpContext });

        Assert.Equal(claimedTenantId, currentUser.OrganizationId);
    }

    [Fact]
    public async Task ScopeMiddleware_WhenOrganizationExists_StoresValidatedScopeAndCachesLookup()
    {
        using var cache = CreateCache();
        var repository = new FakeOrganizationAdministrationRepository(CreateOrganization(TenantId));
        var middleware = new SuperadminOrganizationScopeMiddleware(
            _ => Task.CompletedTask,
            NullLogger<SuperadminOrganizationScopeMiddleware>.Instance);

        var firstContext = CreateHttpContext(Roles.Superadmin, organizationId: null);
        firstContext.Request.Headers[CurrentUserContext.OrganizationScopeHeader] = TenantId.ToString();
        await middleware.InvokeAsync(firstContext, repository, cache);

        var secondContext = CreateHttpContext(Roles.Superadmin, organizationId: null);
        secondContext.Request.Headers[CurrentUserContext.OrganizationScopeHeader] = TenantId.ToString();
        await middleware.InvokeAsync(secondContext, repository, cache);

        Assert.Equal(TenantId, firstContext.Items[CurrentUserContext.ValidatedOrganizationScopeItem]);
        Assert.Equal(TenantId, secondContext.Items[CurrentUserContext.ValidatedOrganizationScopeItem]);
        Assert.Equal(1, repository.GetOrganizationCalls);
    }

    [Fact]
    public async Task ScopeMiddleware_WhenOrganizationIsUnknown_DoesNotStoreScope()
    {
        using var cache = CreateCache();
        var httpContext = CreateHttpContext(Roles.Superadmin, organizationId: null);
        httpContext.Request.Headers[CurrentUserContext.OrganizationScopeHeader] = TenantId.ToString();
        var repository = new FakeOrganizationAdministrationRepository(null);
        var middleware = new SuperadminOrganizationScopeMiddleware(
            _ => Task.CompletedTask,
            NullLogger<SuperadminOrganizationScopeMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext, repository, cache);

        Assert.False(httpContext.Items.ContainsKey(CurrentUserContext.ValidatedOrganizationScopeItem));
        Assert.Equal(1, repository.GetOrganizationCalls);
    }

    [Fact]
    public async Task ScopeMiddleware_ForTenantAdmin_IgnoresHeaderWithoutRepositoryLookup()
    {
        using var cache = CreateCache();
        var claimedTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var httpContext = CreateHttpContext(Roles.Admin, claimedTenantId);
        httpContext.Request.Headers[CurrentUserContext.OrganizationScopeHeader] = TenantId.ToString();
        var repository = new FakeOrganizationAdministrationRepository(CreateOrganization(TenantId));
        var middleware = new SuperadminOrganizationScopeMiddleware(
            _ => Task.CompletedTask,
            NullLogger<SuperadminOrganizationScopeMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext, repository, cache);

        Assert.False(httpContext.Items.ContainsKey(CurrentUserContext.ValidatedOrganizationScopeItem));
        Assert.Equal(0, repository.GetOrganizationCalls);
    }

    [Fact]
    public void JwtHelper_PlatformSuperadminTokenOmitsOrganizationClaim()
    {
        var response = JwtHelper.GenerateToken(
            new AuthUserInfo(Guid.NewGuid(), null, "superadmin@example.test", "Platform Admin", Roles.Superadmin),
            CreateJwtConfiguration());

        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);

        Assert.DoesNotContain(token.Claims, claim => claim.Type == "organizationId");
    }

    [Fact]
    public void JwtHelper_TenantUserTokenIncludesOrganizationClaim()
    {
        var response = JwtHelper.GenerateToken(
            new AuthUserInfo(Guid.NewGuid(), TenantId, "admin@example.test", "Tenant Admin", Roles.Admin),
            CreateJwtConfiguration());

        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);

        Assert.Contains(token.Claims, claim =>
            claim.Type == "organizationId" && claim.Value == TenantId.ToString());
    }

    [Fact]
    public async Task TenantUserValidators_RejectSuperadminRole()
    {
        var createResult = await new CreateUserRequestValidator().ValidateAsync(
            new CreateUserRequest("platform@example.test", "Platform Admin", string.Empty, Roles.Superadmin));
        var updateResult = await new UpdateUserRequestValidator().ValidateAsync(
            new UpdateUserRequest(null, null, Roles.Superadmin));

        Assert.False(createResult.IsValid);
        Assert.False(updateResult.IsValid);
    }

    private static DefaultHttpContext CreateHttpContext(string role, Guid? organizationId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, role)
        };
        if (organizationId is Guid tenantId)
        {
            claims.Add(new Claim("organizationId", tenantId.ToString()));
        }

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
    }

    private static MemoryCache CreateCache() => new(new MemoryCacheOptions());

    private static OrganizationRow CreateOrganization(Guid id) => new()
    {
        Id = id,
        Name = "Selected tenant",
        Cvr = "12345678",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static IConfiguration CreateJwtConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "workslip-tests",
                ["Jwt:Audience"] = "workslip-tests",
                ["Jwt:SigningKey"] = "workslip-platform-superadmin-tests-signing-key-2026"
            })
            .Build();

    private sealed class FakeOrganizationAdministrationRepository(OrganizationRow? organization)
        : IOrganizationAdministrationRepository
    {
        public int GetOrganizationCalls { get; private set; }

        public Task<OrganizationRow?> GetOrganizationAsync(
            Guid organizationId,
            CancellationToken cancellationToken)
        {
            GetOrganizationCalls++;
            return Task.FromResult(
                organization?.Id == organizationId ? organization : null);
        }

        public Task<UserDataRow?> GetUserByEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(null);

        public Task<UserDataRow?> GetUnlinkedAdminAsync(
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<UserDataRow?>(null);

        public Task<bool> IsEntraIdentityReferencedAsync(
            string entraUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<Guid?> CreateAdminAsync(
            UserDataRow admin,
            CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);

        public Task<bool> UpdateAdminAsync(
            UserDataRow admin,
            string expectedEmail,
            string expectedEntraId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
