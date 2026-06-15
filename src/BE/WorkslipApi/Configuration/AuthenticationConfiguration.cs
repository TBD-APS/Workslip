using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Workslip.Api.Helpers;
using Workslip.Application.Auth;

namespace Workslip.Api.Configuration;

public static class AuthenticationConfiguration
{
    private const string CombinedScheme = "Combined";
    private const string LocalJwtScheme = "LocalJwt";
    private const string EntraJwtScheme = "EntraJwt";

    public static WebApplicationBuilder ConfigureAuthentication(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;


        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CombinedScheme;
                options.DefaultAuthenticateScheme = CombinedScheme;
                options.DefaultChallengeScheme = CombinedScheme;
            })
            .AddPolicyScheme(CombinedScheme, "Local JWT or Entra ID", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var authHeader = context.Request.Headers.Authorization.ToString();

                    if (string.IsNullOrWhiteSpace(authHeader) ||
                        !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        return LocalJwtScheme;
                    }

                    var token = authHeader["Bearer ".Length..].Trim();

                    try
                    {
                        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

                        var issuer = jwt.Issuer;

                        var scheme = issuer.Contains("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase) ||
                               issuer.Contains("sts.windows.net", StringComparison.OrdinalIgnoreCase)
                            ? EntraJwtScheme
                            : LocalJwtScheme;

                        return scheme;
                    }
                    catch (SecurityTokenException)
                    {
                        return LocalJwtScheme;
                    }
                };
            })
            .AddJwtBearer(LocalJwtScheme, options =>
            {
                options.TokenValidationParameters =
                    JwtHelper.GetTokenValidationParameters(configuration);

            })
            .AddMicrosoftIdentityWebApi(
        configuration.GetSection("Azure:AdOAuth"),
        jwtBearerScheme: EntraJwtScheme);

        builder.Services.Configure<JwtBearerOptions>(EntraJwtScheme, options =>
        {
            var clientId = configuration["Azure:AdOAuth:ClientId"];

            // Tving middlewaren til at acceptere både det rene GUID og api:// formatet
            options.TokenValidationParameters.ValidAudiences = new[] { clientId, $"api://{clientId}" };

            // Da du kører gæstebrugere/multitenant i bunden, skal issuer-valideringen være fleksibel
            options.TokenValidationParameters.ValidateIssuer = false;
        });

        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddScoped<IClaimsTransformation, UserClaimsTransformation>();

        builder.Services.AddAuthorization();

        builder.Services.AddSingleton<IAuthorizationPolicyProvider, DynamicPolicyProvider>();
        builder.Services.AddSingleton<IAuthorizationHandler, DynamicRoleHandler>();

        return builder;
    }
}
