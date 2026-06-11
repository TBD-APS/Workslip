namespace Workslip.Application.Auth;

public sealed record SendCodeRequest(string Email);

public sealed record VerifyCodeRequest(string Email, string Code);

public sealed record AuthUserInfo(
    Guid UserId,
    Guid OrganizationId,
    string Email,
    string DisplayName,
    string Role);

public sealed record AuthTokenResponse(
    string Token,
    string TokenType,
    int ExpiresIn,
    AuthUserInfo User);

public sealed record VerifyInviteRequest(string Token, string DisplayName, string? Phone);

public sealed record CompleteInviteRequest(string DisplayName, string? Phone);
