using Ardalis.Result;
using Workslip.Application.Users;

namespace Workslip.Application.Auth;

public interface IAuthService
{
    Task<UserResponse> GetCurrentUserAsync(CancellationToken cancellationToken);
    Task SendLoginCodeAsync(SendCodeRequest request, CancellationToken cancellationToken);

    Task<Result<AuthUserInfo>> VerifyLoginCodeAsync(VerifyCodeRequest request, CancellationToken cancellationToken);
    Task<Result<AuthUserInfo>> CompleteEntraLoginAsync(CancellationToken cancellationToken);
}
