using Microsoft.EntityFrameworkCore;
using Workslip.Application.Users;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfInviteRepository : IInviteRepository
{
    private readonly SqlDbContext _dbContext;

    public EfInviteRepository(SqlDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CreateAsync(InviteTokenRow invite, CancellationToken cancellationToken)
    {
        _dbContext.InviteTokens.Add(invite);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<InviteTokenRow?> GetByTokenAsync(string token, CancellationToken cancellationToken)
    {
        return await _dbContext.InviteTokens
            .FirstOrDefaultAsync(i => i.Token == token, cancellationToken);
    }

    public async Task MarkConsumedAsync(Guid id, CancellationToken cancellationToken)
    {
        var invite = await _dbContext.InviteTokens.FindAsync([id], cancellationToken);
        if (invite is not null)
        {
            invite.Consumed = true;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
