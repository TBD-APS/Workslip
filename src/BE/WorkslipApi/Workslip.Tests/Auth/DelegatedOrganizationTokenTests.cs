using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Workslip.Api;
using Workslip.Application.Auth;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Auth;

public sealed class DelegatedOrganizationTokenTests
{
    [Fact]
    public void GenerateOrganizationSessionToken_PreservesActorAndScopesTargetOrganization()
    {
        var userId = Guid.NewGuid();
        var homeOrganizationId = Guid.NewGuid();
        var targetOrganizationId = Guid.NewGuid();
        var user = new AuthUserInfo(
            userId,
            targetOrganizationId,
            "superadmin@example.test",
            "Platform operator",
            Roles.Superadmin);

        var response = JwtHelper.GenerateOrganizationSessionToken(
            user,
            homeOrganizationId,
            CreateConfiguration());

        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);

        Assert.Equal(JwtHelper.DefaultOrganizationSessionExpiryMinutes * 60, response.ExpiresIn);
        Assert.Equal(userId.ToString(), token.Claims.Single(claim => claim.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal(targetOrganizationId.ToString(), token.Claims.Single(claim => claim.Type == "organizationId").Value);
        Assert.Equal(homeOrganizationId.ToString(), token.Claims.Single(claim => claim.Type == JwtHelper.HomeOrganizationIdClaim).Value);
        Assert.Equal("true", token.Claims.Single(claim => claim.Type == JwtHelper.DelegatedOrganizationSessionClaim).Value);
        Assert.Equal(Roles.Superadmin, token.Claims.Single(claim => claim.Type == ClaimTypes.Role).Value);
        Assert.False(string.IsNullOrWhiteSpace(token.Id));
        Assert.Equal(targetOrganizationId, response.User.OrganizationId);
    }

    [Fact]
    public void GenerateOrganizationSessionToken_UsesBoundedConfiguredExpiry()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Jwt:OrganizationSessionExpiryMinutes"] = "10"
        });
        var user = new AuthUserInfo(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "superadmin@example.test",
            "Platform operator",
            Roles.Superadmin);

        var response = JwtHelper.GenerateOrganizationSessionToken(
            user,
            Guid.NewGuid(),
            configuration);

        Assert.Equal(600, response.ExpiresIn);
    }

    private static IConfiguration CreateConfiguration(
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "workslip-tests",
            ["Jwt:Audience"] = "workslip-tests",
            ["Jwt:SigningKey"] = "workslip-test-signing-key-with-at-least-thirty-two-bytes"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
