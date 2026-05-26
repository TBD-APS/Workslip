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

        var tokenSection = GetTokenSection(configuration);
        var expiryMinutes = 60;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenSection.SigningKey));
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
            issuer: tokenSection.Issuer,
            audience: tokenSection.Audience,
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
        var tokenSection = GetTokenSection(configuration); 
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = tokenSection.Issuer,
            ValidAudience = tokenSection.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenSection.SigningKey ?? string.Empty)),
            ClockSkew = TimeSpan.Zero,
        };
    }

    private static TokenSection GetTokenSection(IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");

        return new TokenSection()
        {
            Issuer = jwtSection["Issuer"]!,
            Audience = jwtSection["Audience"]!,
            SigningKey = jwtSection["SigningKey"]!
        };
    }

    private record TokenSection
    {
        public required string Issuer { get; set; }
        public required string Audience { get; set; }
        public required string SigningKey { get; set; }
    }
    
}
