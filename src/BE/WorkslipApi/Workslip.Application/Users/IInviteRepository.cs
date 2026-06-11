using Workslip.Domain.Models;

namespace Workslip.Application.Users;

public interface IInviteRepository
{
    Task CreateAsync(InviteTokenRow invite, CancellationToken cancellationToken);
    Task UpdateAsync(InviteTokenRow invite, CancellationToken cancellationToken);

    Task<InviteTokenRow?> GetByTokenAsync(string token, CancellationToken cancellationToken);

    Task<InviteTokenRow> GetInviteByEmailAsync(Guid organizationId, string email, CancellationToken cancellationToken);
    Task<List<InviteTokenRow>> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task MarkConsumedAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken);

    Task MarkOpenedAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken);
}
