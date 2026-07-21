using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Workslip.Application.Jobs;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfJobViewRepository(ILogger<EfJobViewRepository> logger, SqlDbContext dbContext) : IJobViewRepository
{
    public async Task MarkAsViewedAsync(Guid jobId, Guid userId, string viewType, CancellationToken cancellationToken)
    {
        var existing = await dbContext.JobViews
            .FirstOrDefaultAsync(v => v.JobId == jobId && v.UserId == userId && v.ViewType == viewType, cancellationToken);

        if (existing is not null)
            return;

        var row = new Domain.Models.JobViewRow
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            UserId = userId,
            ViewType = viewType,
            ViewedAt = DateTimeOffset.UtcNow
        };

        try
        {
            dbContext.JobViews.Add(row);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            //Race condition guard 
            logger.LogError("Failed to mark job {jobId} as-seen on user {userId} with type {ViewType}", jobId, userId, viewType);
        }
    }

    public async Task<IReadOnlyList<Guid>> GetViewedJobIdsAsync(Guid userId, IReadOnlyList<Guid> jobIds, string viewType, CancellationToken cancellationToken)
    {
        if (jobIds.Count == 0)
            return [];

        return await dbContext.JobViews
            .AsNoTracking()
            .Where(v => v.UserId == userId && v.ViewType == viewType && jobIds.Contains(v.JobId))
            .Select(v => v.JobId)
            .ToListAsync(cancellationToken);
    }
}
