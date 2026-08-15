using Microsoft.EntityFrameworkCore;
using Workslip.Application.Jobs;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfJobAuditorScopeRepository(SqlDbContext dbContext) : IJobAuditorScopeRepository
{
    public Task<JobAuditorScopeResponse?> GetAsync(
        Guid jobId,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        dbContext.JobReports
            .AsNoTracking()
            .Where(job => job.Id == jobId && job.OrganizationId == organizationId)
            .Select(job => new JobAuditorScopeResponse(job.IsInAuditorScope, job.AuditorScopeReason))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetVisibleJobIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken)
    {
        if (jobIds.Count == 0)
            return new HashSet<Guid>();

        var ids = jobIds.Distinct().ToArray();
        var visibleIds = await dbContext.JobReports
            .AsNoTracking()
            .Where(job => job.OrganizationId == organizationId
                && job.IsInAuditorScope
                && ids.Contains(job.Id))
            .Select(job => job.Id)
            .ToListAsync(cancellationToken);

        return visibleIds.ToHashSet();
    }

    public async Task<JobAuditorScopeResponse?> SetAsync(
        Guid jobId,
        Guid organizationId,
        bool isInAuditorScope,
        string? reason,
        CancellationToken cancellationToken)
    {
        var job = await dbContext.JobReports
            .SingleOrDefaultAsync(
                row => row.Id == jobId && row.OrganizationId == organizationId,
                cancellationToken);
        if (job is null)
            return null;

        if (job.IsInAuditorScope == isInAuditorScope
            && string.Equals(job.AuditorScopeReason, reason, StringComparison.Ordinal))
        {
            return new JobAuditorScopeResponse(job.IsInAuditorScope, job.AuditorScopeReason);
        }

        job.IsInAuditorScope = isInAuditorScope;
        job.AuditorScopeReason = reason;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new JobAuditorScopeResponse(job.IsInAuditorScope, job.AuditorScopeReason);
    }
}
