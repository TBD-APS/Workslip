using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfUserBillingRepository(
    SqlDbContext dbContext,
    ICurrentUserContext currentUser) : IUserBillingRepository
{
    public Task<UserDataRow?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
            return Task.FromResult<UserDataRow?>(null);

        return dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                user => user.Id == userId && user.OrganizationId == organizationId.Value,
                cancellationToken);
    }

    public async Task<bool> SetBillingRateAsync(
        Guid organizationId,
        Guid userId,
        decimal? rate,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId != organizationId)
            return false;

        var user = await dbContext.Users
            .FirstOrDefaultAsync(
                candidate => candidate.Id == userId && candidate.OrganizationId == organizationId,
                cancellationToken);
        if (user is null)
            return false;

        user.BillableHourlyRate = rate;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
