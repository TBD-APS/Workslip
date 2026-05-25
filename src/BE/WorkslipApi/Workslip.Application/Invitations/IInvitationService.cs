using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Users;

namespace Workslip.Application.Invitations;

public interface IInvitationService
{
    Task<InviteUsersResponse> InviteUsersAsync(InviteUsersRequest request, CancellationToken cancellationToken);

    Task<Result<AuthUserInfo>> VerifyInviteAsync(VerifyInviteRequest request, CancellationToken cancellationToken);
}
