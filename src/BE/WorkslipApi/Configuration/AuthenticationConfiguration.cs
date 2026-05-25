using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

namespace Workslip.Api.Configuration;

public static class AuthenticationConfiguration
{
    public static WebApplicationBuilder ConfigureAuthentication(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("Azure:AdOAuth"));

        builder.Services.AddAuthentication()
            .AddJwtBearer("LocalJwt", options =>
            {
                options.TokenValidationParameters = JwtHelper.GetTokenValidationParameters(configuration);
            });

        builder.Services.AddAuthorization();

        builder.Services.AddSingleton<IAuthorizationPolicyProvider, DynamicPolicyProvider>();
        builder.Services.AddSingleton<IAuthorizationHandler, DynamicRoleHandler>();

        return builder;
    }
}
