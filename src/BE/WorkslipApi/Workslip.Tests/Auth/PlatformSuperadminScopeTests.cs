using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Workslip.Api;
using Workslip.Api.Helpers;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Application.Users.Validators;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Auth;

public sealed class PlatformSuperadminScopeTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void CurrentUserContext_SuperadminUsesExplicitHeaderAndIgnoresLegacyClaim()
    {
        var legacyTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var httpContext = CreateHttpContext(Roles.Superadmin, legacyTenantId);
        httpContext.Request.Headers[CurrentUserContext.OrganizationScopeHeader] = TenantId.ToString();

        var currentUser = new CurrentUserContext(new HttpContextAccessor { HttpContext = httpContext });

        Assert.Equal(TenantId, currentUser.OrganizationId);
    }

    [Fact]
    public void CurrentUserContext_SuperadminWithoutHeaderHasNoOrganizationScope()
    {
        var httpContext = CreateHttpContext(Roles.Superadmin, TenantId);
        var currentUser = new CurrentUserContext(new HttpContextAccessor { HttpContext = httpContext });

        Assert.Null(currentUser.OrganizationId);
    }

    [Fact]
    public void CurrentUserContext_TenantAdminIgnoresScopeHeader()
    {
        var claimedTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var httpContext = CreateHttpContext(Roles.Admin, claimedTenantId);
        httpContext.Request.Headers[CurrentUserContext.OrganizationScopeHeader] = TenantId.ToString();

        var currentUser = new CurrentUserContext(new HttpContextAccessor { HttpContext = httpContext });

        Assert.Equal(claimedTenantId, currentUser.OrganizationId);
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

    private static IConfiguration CreateJwtConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "workslip-tests",
                ["Jwt:Audience"] = "workslip-tests",
                ["Jwt:SigningKey"] = "workslip-platform-superadmin-tests-signing-key-2026"
            })
            .Build();
}
