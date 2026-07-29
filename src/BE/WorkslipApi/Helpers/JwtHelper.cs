using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Workslip.Application.Auth;

namespace Workslip.Api;

public static class JwtHelper
{
    public const int DefaultExpiryMinutes = 60;
    public const int DefaultOrganizationSessionExpiryMinutes = 15;
    public const string HomeOrganizationIdClaim = "homeOrganizationId";
    public const string DelegatedOrganizationSessionClaim = "delegatedOrganizationSession";

    public static AuthTokenResponse GenerateToken(AuthUserInfo user, IConfiguration configuration) =>
        GenerateTokenCore(
            user,
            configuration,
            ResolveExpiryMinutes(configuration),
            []);

    public static AuthTokenResponse GenerateOrganizationSessionToken(
        AuthUserInfo user,
        Guid homeOrganizationId,
        IConfiguration configuration) =>
        GenerateTokenCore(
            user,
            configuration,
            ResolveOrganizationSessionExpiryMinutes(configuration),
            [
                new Claim(HomeOrganizationIdClaim, homeOrganizationId.ToString()),
                new Claim(DelegatedOrganizationSessionClaim, bool.TrueString.ToLowerInvariant()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            ]);

    private static AuthTokenResponse GenerateTokenCore(
        AuthUserInfo user,
        IConfiguration configuration,
        int expiryMinutes,
        IReadOnlyCollection<Claim> additionalClaims)
    {
        var tokenSection = GetTokenSection(configuration);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenSection.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            new("organizationId", user.OrganizationId.ToString()),
            new(ClaimTypes.Role, user.Role)
        };
        claims.AddRange(additionalClaims);

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes);
        var token = new JwtSecurityToken(
            issuer: tokenSection.Issuer,
            audience: tokenSection.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AuthTokenResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            "Bearer",
            expiryMinutes * 60,
            user);
    }

    private static int ResolveExpiryMinutes(IConfiguration configuration)
    {
        var raw = configuration["Jwt:ExpiryMinutes"];
        return int.TryParse(raw, out var minutes) && minutes > 0
            ? minutes
            : DefaultExpiryMinutes;
    }

    private static int ResolveOrganizationSessionExpiryMinutes(IConfiguration configuration)
    {
        var raw = configuration["Jwt:OrganizationSessionExpiryMinutes"];
        return int.TryParse(raw, out var minutes) && minutes is >= 1 and <= 30
            ? minutes
            : DefaultOrganizationSessionExpiryMinutes;
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

        return new TokenSection
        {
            Issuer = jwtSection["Issuer"]!,
            Audience = jwtSection["Audience"]!,
            SigningKey = jwtSection["SigningKey"]!
        };
    }

    private sealed record TokenSection
    {
        public required string Issuer { get; init; }
        public required string Audience { get; init; }
        public required string SigningKey { get; init; }
    }
}
