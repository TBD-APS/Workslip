using Ardalis.Result;

namespace Workslip.Application.Invitations;

public interface IInvitationStatusService
{
    Task<Result> ClearAsync(Guid inviteId, CancellationToken cancellationToken);
}
