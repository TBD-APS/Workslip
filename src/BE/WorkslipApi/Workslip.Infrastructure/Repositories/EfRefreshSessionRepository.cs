using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfRefreshSessionRepository(SqlDbContext db) : IRefreshSessionRepository
{
    public async Task CreateAsync(RefreshSessionRow session, CancellationToken cancellationToken)
    {
        db.RefreshSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<RefreshSessionRow?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.RefreshSessions.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

    public Task<AuthUserInfo?> GetEligibleUserAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken) =>
        (from user in db.Users.AsNoTracking()
         join organization in db.Organizations.AsNoTracking() on user.OrganizationId equals organization.Id
         where user.Id == userId && user.OrganizationId == organizationId
         select new AuthUserInfo(user.Id, user.OrganizationId, user.Email, user.DisplayName, user.Role))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> TryRotateAsync(
        RefreshSessionRow current,
        RefreshSessionRow replacement,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var nextStamp = Guid.NewGuid();
        var affected = await db.RefreshSessions
            .Where(x => x.Id == current.Id
                && x.ConcurrencyStamp == current.ConcurrencyStamp
                && x.UsedAt == null
                && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.UsedAt, usedAt)
                .SetProperty(x => x.ConcurrencyStamp, nextStamp), cancellationToken);

        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        db.RefreshSessions.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryClaimConcurrentRefreshGraceAsync(
        Guid sessionId,
        Guid expectedConcurrencyStamp,
        DateTimeOffset graceReuseAt,
        CancellationToken cancellationToken)
    {
        var affected = await db.RefreshSessions
            .Where(x => x.Id == sessionId
                && x.ConcurrencyStamp == expectedConcurrencyStamp
                && x.UsedAt != null
                && x.GraceReuseAt == null
                && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.GraceReuseAt, graceReuseAt)
                .SetProperty(x => x.ConcurrencyStamp, Guid.NewGuid()), cancellationToken);
        return affected == 1;
    }

    public Task RevokeFamilyAsync(Guid familyId, DateTimeOffset revokedAt, CancellationToken cancellationToken) =>
        db.RefreshSessions
            .Where(x => x.FamilyId == familyId && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAt, revokedAt)
                .SetProperty(x => x.ConcurrencyStamp, Guid.NewGuid()), cancellationToken);

    public Task<int> DeleteExpiredFamiliesAsync(DateTimeOffset deleteBefore, CancellationToken cancellationToken) =>
        db.RefreshSessions
            .Where(x => x.ExpiresAt <= deleteBefore || (x.RevokedAt != null && x.RevokedAt <= deleteBefore))
            .ExecuteDeleteAsync(cancellationToken);
}
