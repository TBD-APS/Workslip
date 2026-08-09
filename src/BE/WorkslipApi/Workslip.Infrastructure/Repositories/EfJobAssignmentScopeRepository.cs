using Microsoft.EntityFrameworkCore;
using Workslip.Application.Jobs;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfJobAssignmentScopeRepository(SqlDbContext dbContext) : IJobAssignmentScopeRepository
{
    public async Task<Guid?> GetDefaultFilialIdAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<OrganizationFilialRow>()
            .AsNoTracking()
            .Where(filial => filial.OrganizationId == organizationId && filial.IsDefault)
            .Select(filial => (Guid?)filial.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> GetJobFilialIdAsync(
        Guid organizationId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        return await dbContext.JobReports
            .AsNoTracking()
            .Where(job => job.OrganizationId == organizationId && job.Id == jobId)
            .Select(job => (Guid?)job.FilialId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<JobAssignmentUserScope>> GetUserScopesAsync(
        Guid organizationId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var normalizedUserIds = userIds.Distinct().ToArray();
        return await dbContext.Users
            .AsNoTracking()
            .Where(user => user.OrganizationId == organizationId && normalizedUserIds.Contains(user.Id))
            .Select(user => new JobAssignmentUserScope(user.Id, user.FilialId, user.Role))
            .ToArrayAsync(cancellationToken);
    }
}
