using Workslip.Domain.Models;

namespace Workslip.Application.Users;

public interface IInviteRepository
{
    Task CreateAsync(InviteTokenRow invite, CancellationToken cancellationToken);

    Task<InviteTokenRow?> GetByTokenAsync(string token, CancellationToken cancellationToken);

    Task MarkConsumedAsync(Guid id, CancellationToken cancellationToken);
}
