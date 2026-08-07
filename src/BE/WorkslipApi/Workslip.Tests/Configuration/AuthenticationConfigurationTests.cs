using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Workslip.Api.Configuration;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class AuthenticationConfigurationTests
{
    [Fact]
    public void ConfigureAuthentication_EntraSchemeHasIssuerValidation()
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

        Assert.True(
            validation.ValidateIssuer || hasCustomIssuerValidator,
            "EntraJwt must validate the token issuer through the default or a custom issuer validator.");
    }
}
