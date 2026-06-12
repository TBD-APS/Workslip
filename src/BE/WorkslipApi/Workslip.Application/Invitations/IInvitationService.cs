using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Users;

namespace Workslip.Application.Invitations;

public interface IInvitationService
{
    Task<Result<InviteUsersResponse>> InviteUsersAsync(InviteUsersRequest request, CancellationToken cancellationToken);

    Task<Result<AuthUserInfo>> VerifyInviteAsync(VerifyInviteRequest request, CancellationToken cancellationToken);

    Task<Result<AuthUserInfo>> CompleteEnrollmentAsync(EntraEnrollRequest request, CancellationToken cancellationToken);

    Task<Result<InviteListResponse>> GetOrganizationInvitesAsync(CancellationToken cancellationToken);

    Task<Result> MarkOpenedAsync(string token, CancellationToken cancellationToken);
}
