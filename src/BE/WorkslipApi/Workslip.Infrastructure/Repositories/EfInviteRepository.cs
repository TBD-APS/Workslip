using Microsoft.EntityFrameworkCore;
using Workslip.Application.Invitations;
using Workslip.Application.Users;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfInviteRepository : IInviteRepository, IInvitationStatusRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;

    public EfInviteRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
    }

    public Task CreateAsync(InviteTokenRow invite, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("invites.create", token => CreateCoreAsync(invite, token), cancellationToken);

    public Task UpdateAsync(InviteTokenRow invite, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("invites.update", token => UpdateCoreAsync(invite, token), cancellationToken);

    public Task<InviteTokenRow?> GetInviteByEmailAsync(Guid organizationId, string email, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("invites.get-by-email", token => GetInviteByEmailCoreAsync(organizationId, email, token), cancellationToken);

    public Task<InviteTokenRow?> GetByIdAsync(Guid organizationId, Guid inviteId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("invites.get-by-id", token => GetByIdCoreAsync(organizationId, inviteId, token), cancellationToken);

    public Task<InviteTokenRow?> GetByTokenAsync(string token, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("invites.get-by-token", ct => GetByTokenCoreAsync(token, ct), cancellationToken);

    public Task<List<InviteTokenRow>> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("invites.get-by-org", token => GetByOrganizationCoreAsync(organizationId, token), cancellationToken);

    public Task MarkConsumedAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("invites.mark-consumed", token => MarkConsumedCoreAsync(inviteTokenRow, token), cancellationToken);

    public Task MarkOpenedAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("invites.mark-opened", token => MarkOpenedCoreAsync(inviteTokenRow, token), cancellationToken);

    public Task<IReadOnlyList<InviteTokenRow>> GetStaleEntraProvisionedAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("invites.stale-entra", token => GetStaleEntraProvisionedCoreAsync(now, take, token), cancellationToken);

    public Task<bool> TryRevokePendingAsync(
        Guid organizationId,
        Guid inviteId,
        string currentToken,
        DateTimeOffset revokedAt,
        string replacementToken,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "invites.try-revoke",
            token => TryRevokePendingCoreAsync(organizationId, inviteId, currentToken, revokedAt, replacementToken, token),
            cancellationToken);

    public Task<bool> TryDeleteAsync(
        Guid organizationId,
        Guid inviteId,
        string token,
        bool consumed,
        DateTimeOffset? revokedAt,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "invites.try-delete",
            ct => TryDeleteCoreAsync(organizationId, inviteId, token, consumed, revokedAt, ct),
            cancellationToken);

    private async Task CreateCoreAsync(InviteTokenRow invite, CancellationToken cancellationToken)
    {
        _dbContext.InviteTokens.Add(invite);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UpdateCoreAsync(InviteTokenRow invite, CancellationToken cancellationToken)
    {
        _dbContext.InviteTokens.Update(invite);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<InviteTokenRow?> GetInviteByEmailCoreAsync(Guid organizationId, string email, CancellationToken cancellationToken)
    {
        return await _dbContext.InviteTokens
            .FirstOrDefaultAsync(
                invite => invite.OrganizationId == organizationId
                    && invite.Email == email
                    && invite.RevokedAt == null,
                cancellationToken);
    }

    private async Task<InviteTokenRow?> GetByIdCoreAsync(Guid organizationId, Guid inviteId, CancellationToken cancellationToken)
    {
        return await _dbContext.InviteTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(
                invite => invite.OrganizationId == organizationId && invite.Id == inviteId,
                cancellationToken);
    }

    private async Task<InviteTokenRow?> GetByTokenCoreAsync(string token, CancellationToken cancellationToken)
    {
        return await _dbContext.InviteTokens
            .FirstOrDefaultAsync(i => i.Token == token && i.RevokedAt == null, cancellationToken);
    }

    private async Task<List<InviteTokenRow>> GetByOrganizationCoreAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        return await _dbContext.InviteTokens
            .Where(i => i.OrganizationId == organizationId)
            .OrderByDescending(i => !i.Consumed)
            .ToListAsync(cancellationToken);
    }

    private async Task MarkConsumedCoreAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken)
    {
        var acceptedAt = DateTimeOffset.UtcNow;
        var affectedRows = await _dbContext.InviteTokens
            .Where(invite => invite.Id == inviteTokenRow.Id
                && invite.OrganizationId == inviteTokenRow.OrganizationId
                && invite.Token == inviteTokenRow.Token
                && !invite.Consumed
                && invite.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(invite => invite.Consumed, true)
                    .SetProperty(invite => invite.AcceptedAt, acceptedAt),
                cancellationToken);

        if (affectedRows == 0)
        {
            throw new InviteStateChangedException(inviteTokenRow.Id);
        }

        inviteTokenRow.Consumed = true;
        inviteTokenRow.AcceptedAt = acceptedAt;
    }

    private async Task MarkOpenedCoreAsync(InviteTokenRow inviteTokenRow, CancellationToken cancellationToken)
    {
        inviteTokenRow.OpenedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<InviteTokenRow>> GetStaleEntraProvisionedCoreAsync(DateTimeOffset now, int take, CancellationToken cancellationToken)
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

    private async Task<bool> TryRevokePendingCoreAsync(
        Guid organizationId,
        Guid inviteId,
        string currentToken,
        DateTimeOffset revokedAt,
        string replacementToken,
        CancellationToken cancellationToken)
    {
        var affectedRows = await _dbContext.InviteTokens
            .Where(invite => invite.OrganizationId == organizationId
                && invite.Id == inviteId
                && invite.Token == currentToken
                && !invite.Consumed
                && invite.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(invite => invite.RevokedAt, revokedAt)
                    .SetProperty(invite => invite.ExpiresAt, revokedAt)
                    .SetProperty(invite => invite.Token, replacementToken),
                cancellationToken);

        return affectedRows == 1;
    }

    private async Task<bool> TryDeleteCoreAsync(
        Guid organizationId,
        Guid inviteId,
        string token,
        bool consumed,
        DateTimeOffset? revokedAt,
        CancellationToken cancellationToken)
    {
        var affectedRows = await _dbContext.InviteTokens
            .Where(invite => invite.OrganizationId == organizationId
                && invite.Id == inviteId
                && invite.Token == token
                && invite.Consumed == consumed
                && invite.RevokedAt == revokedAt)
            .ExecuteDeleteAsync(cancellationToken);

        return affectedRows == 1;
    }
}
