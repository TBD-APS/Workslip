using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using Workslip.Api.Configuration;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class AuthenticationConfigurationTests
{
    [Fact]
    public void ConfigureAuthentication_EntraSchemeKeepsDefaultAndCustomIssuerValidation()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Azure:AdOAuth:Instance"] = "https://login.microsoftonline.com/",
            ["Azure:AdOAuth:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["Azure:AdOAuth:ClientId"] = "22222222-2222-2222-2222-222222222222"
        });

        builder.ConfigureAuthentication();

        using var services = builder.Services.BuildServiceProvider();
        var options = services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get("EntraJwt");
        var validation = options.TokenValidationParameters;
        var hasCustomIssuerValidator = validation.IssuerValidator is not null
            || validation.IssuerValidatorUsingConfiguration is not null;

        Assert.True(validation.ValidateIssuer);
        Assert.True(
            hasCustomIssuerValidator,
            "Microsoft.Identity.Web must keep its tenant-aware issuer validator on EntraJwt.");
    }

    [Fact]
    public void ConfigureAuthentication_EntraNotConfigured_RegistersDenyAllEntraScheme()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.ConfigureAuthentication();

        using var services = builder.Services.BuildServiceProvider();
        var options = services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get("EntraJwt");
        var validation = options.TokenValidationParameters;

        Assert.True(validation.ValidateIssuer);
        Assert.True(validation.ValidateAudience);
        Assert.True(validation.ValidateIssuerSigningKey);
        Assert.NotNull(validation.IssuerSigningKey);
        Assert.Equal("entra-jwt-not-configured", validation.ValidIssuer);
    }

    [Theory]
    [InlineData("Bearer not-a-jwt")]
    [InlineData("Bearer eyJub3QiOiJ2YWxpZCJ9.invalid-base64.signature")]
    public void ConfigureAuthentication_MalformedBearer_RoutesToLocalJwtWithoutThrowing(string authorizationHeader)
    {
        using var services = BuildAuthenticationServices();
        var selector = GetCombinedSchemeSelector(services);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = authorizationHeader;

        var scheme = selector(context);

        Assert.Equal("LocalJwt", scheme);
    }

    [Fact]
    public void ConfigureAuthentication_LocalIssuer_RoutesToLocalJwt()
    {
        using var services = BuildAuthenticationServices();
        var selector = GetCombinedSchemeSelector(services);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {CreateUnsignedJwt("https://workslip.local")}";

        var scheme = selector(context);

        Assert.Equal("LocalJwt", scheme);
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0")]
    [InlineData("https://sts.windows.net/11111111-1111-1111-1111-111111111111/")]
    public void ConfigureAuthentication_EntraIssuer_RoutesToEntraJwt(string issuer)
    {
        using var services = BuildAuthenticationServices();
        var selector = GetCombinedSchemeSelector(services);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {CreateUnsignedJwt(issuer)}";

        var scheme = selector(context);

        Assert.Equal("EntraJwt", scheme);
    }

    private static ServiceProvider BuildAuthenticationServices()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.ConfigureAuthentication();
        return builder.Services.BuildServiceProvider();
    }

    private static Func<HttpContext, string?> GetCombinedSchemeSelector(IServiceProvider services)
    {
        var options = services
            .GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>()
            .Get("Combined");

        return Assert.IsType<Func<HttpContext, string?>>(options.ForwardDefaultSelector);
    }

    private static string CreateUnsignedJwt(string issuer)
    {
        var token = new JwtSecurityToken(issuer: issuer);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
