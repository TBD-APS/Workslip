using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

namespace Workslip.Api.Configuration;

public static class AuthenticationConfiguration
{
    public static WebApplicationBuilder ConfigureAuthentication(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = "Combined";
                options.DefaultChallengeScheme = "Combined";
            })
            .AddPolicyScheme("Combined", "LocalJwt or Entra ID", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var authHeader = (string?)context.Request.Headers["Authorization"];
                    if (authHeader is null || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        return "LocalJwt";

                    try
                    {
                        var token = authHeader["Bearer ".Length..].Trim();
                        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
                        return jwt.Header.Kid is not null
                            ? JwtBearerDefaults.AuthenticationScheme
                            : "LocalJwt";
                    }
                    catch
                    {
                        return "LocalJwt";
                    }
                };
            })
            .AddJwtBearer("LocalJwt", options =>
            {
                options.TokenValidationParameters = JwtHelper.GetTokenValidationParameters(configuration);
            });

        builder.Services
            .AddAuthentication()
            .AddMicrosoftIdentityWebApi(configuration.GetSection("Azure:AdOAuth"));

        builder.Services.AddAuthorization();

        builder.Services.AddSingleton<IAuthorizationPolicyProvider, DynamicPolicyProvider>();
        builder.Services.AddSingleton<IAuthorizationHandler, DynamicRoleHandler>();

        return builder;
    }
}
