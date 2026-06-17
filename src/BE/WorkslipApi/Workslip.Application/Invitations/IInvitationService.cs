using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Users;

namespace Workslip.Application.Invitations;

public interface IInvitationService
{
    Task<Result<InviteUsersResponse>> InviteUsersAsync(InviteUsersRequest request, CancellationToken cancellationToken);

    Task<Result<AuthUserInfo>> CompleteEnrollmentAsync(EntraEnrollRequest request, CancellationToken cancellationToken);

    Task<Result<InviteListResponse>> GetOrganizationInvitesAsync(CancellationToken cancellationToken);

    Task<Result<InviteOpenResponse>> MarkOpenedAsync(string token, CancellationToken cancellationToken);

    Task<int> CleanupStaleEntraInvitesAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);
}
