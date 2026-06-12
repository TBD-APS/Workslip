using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Users;

namespace Workslip.Application.Auth;

public sealed class AuthService(
    ICurrentUserContext currentUser,
    IUserRepository userRepository,
    IEmailService emailService,
    ILogger<AuthService> logger) : IAuthService
{
    private static readonly ConcurrentDictionary<string, OtcEntry> _otcStore = new();
    private static readonly TimeSpan OtcTtl = TimeSpan.FromMinutes(10);
    private const int OtcLength = 6;

    public async Task<UserResponse> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException("User is not logged in");
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            logger.LogError("Current user not found in database. UserId: {UserId}", userId);
            throw new UnauthorizedAccessException("User is not logged in");
        }

        var userResponse = UserResponseBuilder.MapToResponse(user);

        return userResponse;
    }

    public async Task SendLoginCodeAsync(SendCodeRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await userRepository.GetByEmailAsync(email, cancellationToken);

        if (user is null)
        {
            logger.LogInformation("Login code requested for unknown email: {Email}", email);
            return;
        }

        var code = GenerateCode();
        var codeHash = HashCode(code);
        var expiresAt = DateTimeOffset.UtcNow.Add(OtcTtl);

        _otcStore[email] = new OtcEntry(codeHash, expiresAt);

        await emailService.SendOtcEmailAsync(email, code, cancellationToken);

        logger.LogInformation("Login code sent to {Email}. ExpiresAt: {ExpiresAt}", email, expiresAt);
    }

    public Task<Result<AuthUserInfo>> VerifyLoginCodeAsync(VerifyCodeRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (!_otcStore.TryRemove(email, out var entry))
        {
            logger.LogWarning("Login code verification failed: no code requested for {Email}", email);
            return Task.FromResult(Result<AuthUserInfo>.Unauthorized());
        }

        if (DateTimeOffset.UtcNow > entry.ExpiresAt)
        {
            logger.LogWarning("Login code verification failed: code expired for {Email}", email);
            return Task.FromResult(Result<AuthUserInfo>.Unauthorized());
        }

        var inputHash = HashCode(request.Code.Trim());
        if (!CryptographicOperations.FixedTimeEquals(entry.CodeHash, inputHash))
        {
            logger.LogWarning("Login code verification failed: invalid code for {Email}", email);
            return Task.FromResult(Result<AuthUserInfo>.Unauthorized());
        }

        return ResolveUserAsync(email, cancellationToken);
    }

    public async Task<Result<AuthUserInfo>> CompleteEntraLoginAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var organizationId = currentUser.OrganizationId;
        if (userId is null || organizationId is null)
        {
            logger.LogWarning("Entra login failed: authenticated Entra user was not mapped to a Workslip user.");
            return Result<AuthUserInfo>.Unauthorized();
        }

        var user = await userRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Entra login failed: mapped Workslip user was not found. UserId: {UserId}.", userId);
            return Result<AuthUserInfo>.Unauthorized();
        }

        return Result<AuthUserInfo>.Success(new AuthUserInfo(
            user.Id,
            user.OrganizationId,
            user.Email,
            user.DisplayName,
            user.Role));
    }

    private async Task<Result<AuthUserInfo>> ResolveUserAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            logger.LogError("User not found after successful code verification: {Email}", email);
            return Result<AuthUserInfo>.Unauthorized();
        }

        return Result<AuthUserInfo>.Success(new AuthUserInfo(
            user.Id,
            user.OrganizationId,
            user.Email,
            user.DisplayName,
            user.Role));
    }

    private static string GenerateCode()
    {
        var bytes = new byte[4];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt32(bytes) % 1_000_000;
        return value.ToString($"D{OtcLength}");
    }

    private static byte[] HashCode(string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(code));

    private sealed record OtcEntry(byte[] CodeHash, DateTimeOffset ExpiresAt);
}
