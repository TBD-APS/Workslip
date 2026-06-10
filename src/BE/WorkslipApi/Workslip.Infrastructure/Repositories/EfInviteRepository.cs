using Microsoft.EntityFrameworkCore;
using Workslip.Application.Users;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

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

    public async Task<List<InviteTokenRow>> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        return await _dbContext.InviteTokens
            .Where(i => i.OrganizationId == organizationId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkConsumedAsync(Guid id, CancellationToken cancellationToken)
    {
        var invite = await _dbContext.InviteTokens.FindAsync([id], cancellationToken);
        if (invite is not null)
        {
            invite.Consumed = true;
            invite.AcceptedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkOpenedAsync(Guid id, CancellationToken cancellationToken)
    {
        var invite = await _dbContext.InviteTokens.FindAsync([id], cancellationToken);
        if (invite is not null && invite.OpenedAt is null)
        {
            invite.OpenedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
