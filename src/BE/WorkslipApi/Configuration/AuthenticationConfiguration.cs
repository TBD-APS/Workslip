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


        var authenticationBuilder = builder.Services
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

            });

        var entraClientId = configuration["Azure:AdOAuth:ClientId"];
        if (string.IsNullOrWhiteSpace(entraClientId))
        {
            // Entra is not configured (deterministic local development without
            // external services). Register a deny-all EntraJwt scheme so
            // Entra-only endpoints fail closed with 401 instead of throwing
            // 500 inside the authentication middleware.
            authenticationBuilder.AddJwtBearer(EntraJwtScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = "entra-jwt-not-configured",
                    ValidateAudience = true,
                    ValidAudience = "entra-jwt-not-configured",
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                };
            });
        }
        else
        {
            authenticationBuilder.AddMicrosoftIdentityWebApi(
                configuration.GetSection("Azure:AdOAuth"),
                jwtBearerScheme: EntraJwtScheme);

            builder.Services.Configure<JwtBearerOptions>(EntraJwtScheme, options =>
            {
                // Accept both audience formats emitted for this API while keeping
                // Microsoft.Identity.Web's tenant/issuer validation enabled.
                options.TokenValidationParameters.ValidAudiences = new[] { entraClientId, $"api://{entraClientId}" };
            });
        }

        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddScoped<IClaimsTransformation, UserClaimsTransformation>();
        builder.Services.AddSingleton<IUserClaimsCacheInvalidator, UserClaimsCacheInvalidator>();

        builder.Services.AddAuthorization();

        builder.Services.AddSingleton<IAuthorizationPolicyProvider, DynamicPolicyProvider>();
        builder.Services.AddSingleton<IAuthorizationHandler, DynamicRoleHandler>();

        return builder;
    }
}
