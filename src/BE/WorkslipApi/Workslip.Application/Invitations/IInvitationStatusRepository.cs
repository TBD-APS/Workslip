using Workslip.Domain.Models;

namespace Workslip.Application.Invitations;

public interface IInvitationStatusRepository
{
    Task<InviteTokenRow?> GetByIdAsync(Guid organizationId, Guid inviteId, CancellationToken cancellationToken);

    Task<bool> TryRevokePendingAsync(
        Guid organizationId,
        Guid inviteId,
        string currentToken,
        DateTimeOffset revokedAt,
        string replacementToken,
        CancellationToken cancellationToken);

    Task<bool> TryDeleteAsync(
        Guid organizationId,
        Guid inviteId,
        string token,
        bool consumed,
        DateTimeOffset? revokedAt,
        CancellationToken cancellationToken);
}
