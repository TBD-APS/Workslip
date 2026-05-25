using Ardalis.Result;

namespace Workslip.Application.Auth;

public interface IAuthService
{
    Task SendLoginCodeAsync(SendCodeRequest request, CancellationToken cancellationToken);

    Task<Result<AuthUserInfo>> VerifyLoginCodeAsync(VerifyCodeRequest request, CancellationToken cancellationToken);
}
