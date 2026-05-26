using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Workslip.Application.Auth;

namespace Workslip.Api;

public static class JwtHelper
{
    public static AuthTokenResponse GenerateToken(AuthUserInfo user, IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var signingKey = jwtSection["SigningKey"]!;
        var issuer = jwtSection["Issuer"]!;
        var audience = jwtSection["Audience"]!;
        var expiryMinutes = 60;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim("organizationId", user.OrganizationId.ToString()),
            new Claim(ClaimTypes.Role, user.Role),
        };

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return new AuthTokenResponse(
            tokenString,
            "Bearer",
            expiryMinutes * 60,
            user);
    }

    public static TokenValidationParameters GetTokenValidationParameters(IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");

        var issuer = jwtSection["Issuer"]!;
        var audience = jwtSection["Audience"]!;
        var signingKey = jwtSection["SigningKey"]!;

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? string.Empty)),
            ClockSkew = TimeSpan.Zero,
        };
    }
}
