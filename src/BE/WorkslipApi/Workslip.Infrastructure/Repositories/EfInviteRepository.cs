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

    public async Task UpdateAsync(InviteTokenRow invite, CancellationToken cancellationToken)
    {
        _dbContext.InviteTokens.Update(invite);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }


    public async Task<InviteTokenRow> GetInviteByEmailAsync(Guid organizationId, string email, CancellationToken cancellationToken)
    {
        var inviteToken = await _dbContext.InviteTokens.FirstOrDefaultAsync(x => x.Email.Equals(email), cancellationToken);
        return inviteToken;
    }

    public async Task<InviteTokenRow?> GetByTokenAsync(string token, CancellationToken cancellationToken)
    {
        return await _dbContext.InviteTokens
            .FirstOrDefaultAsync(i => i.Token == token, cancellationToken);
    }

    public async Task<List<InviteTokenRow>> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var invites = await _dbContext.InviteTokens
            .Where(i => i.OrganizationId == organizationId)
            .OrderByDescending(i => !i.Consumed).ToListAsync();

        return invites.DistinctBy(x => x.Consumed).ToList();
    }

    public async Task MarkConsumedAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken)
    {
        inviteTokenRow.Consumed = true;
        inviteTokenRow.AcceptedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkOpenedAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken)
    {
        inviteTokenRow.OpenedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InviteTokenRow>> GetStaleEntraProvisionedAsync(DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        return await _dbContext.InviteTokens
            .Where(i => !i.Consumed
                && i.EntraCreatedByInvite
                && i.EntraCleanedAt == null
                && i.EntraUserId != null
                && i.ExpiresAt < now)
            .OrderBy(i => i.ExpiresAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
