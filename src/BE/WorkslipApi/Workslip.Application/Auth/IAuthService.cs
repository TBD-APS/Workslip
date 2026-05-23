namespace Workslip.Application.Auth;

public interface IAuthService
{
    Task SendLoginCodeAsync(SendCodeRequest request, CancellationToken cancellationToken);

    Task<AuthUserInfo?> VerifyLoginCodeAsync(VerifyCodeRequest request, CancellationToken cancellationToken);
}
