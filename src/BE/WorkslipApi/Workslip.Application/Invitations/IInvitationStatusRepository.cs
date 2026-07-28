using Workslip.Domain.Models;

namespace Workslip.Application.Invitations;

public interface IInvitationStatusRepository
{
    Task<InviteTokenRow?> GetByIdAsync(Guid organizationId, Guid inviteId, CancellationToken cancellationToken);

    Task DeleteAsync(InviteTokenRow invite, CancellationToken cancellationToken);
}
