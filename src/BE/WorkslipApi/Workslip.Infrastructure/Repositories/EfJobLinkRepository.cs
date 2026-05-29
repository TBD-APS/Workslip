using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfJobLinkRepository : IJobLinkRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;
    private readonly ICurrentUserContext _currentUser;

    public EfJobLinkRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy, ICurrentUserContext currentUser)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
        _currentUser = currentUser;
    }

    public Task<JobLinkResponse> CreateLinkAsync(Guid organizationId, Guid sourceReportId, Guid targetReportId, string linkType, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("links.create", token => CreateLinkAsyncCoreAsync(organizationId, sourceReportId, targetReportId, linkType, token), cancellationToken);

    private async Task<JobLinkResponse> CreateLinkAsyncCoreAsync(Guid organizationId, Guid sourceReportId, Guid targetReportId, string linkType, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            throw new InvalidOperationException("Organization mismatch");

        var normalisedSource = sourceReportId.CompareTo(targetReportId) < 0 ? sourceReportId : targetReportId;
        var normalisedTarget = sourceReportId.CompareTo(targetReportId) < 0 ? targetReportId : sourceReportId;
        var now = DateTimeOffset.UtcNow;

        var linkedReport = await _dbContext.JobReports
            .AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && r.Id == targetReportId)
            .Select(r => new { r.ReportNumber, r.Status, r.CustomerId })
            .FirstOrDefaultAsync(cancellationToken);

        var customerName = linkedReport?.CustomerId is not null
            ? await _dbContext.Customers
                .AsNoTracking()
                .Where(c => c.OrganizationId == organizationId && c.Id == linkedReport.CustomerId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var link = new JobReportLinkRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            SourceReportId = normalisedSource,
            TargetReportId = normalisedTarget,
            LinkType = linkType,
            CreatedAt = now
        };

        _dbContext.JobReportLinks.Add(link);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new JobLinkResponse(
            link.Id,
            sourceReportId,
            targetReportId,
            linkedReport?.ReportNumber ?? string.Empty,
            customerName ?? string.Empty,
            linkedReport?.Status ?? string.Empty,
            linkType,
            now);
    }

    public Task<JobLinkResponse?> GetLinkAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("links.get", token => GetLinkAsyncCoreAsync(organizationId, linkId, token), cancellationToken);

    private async Task<JobLinkResponse?> GetLinkAsyncCoreAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            return null;

        var row = await _dbContext.JobReportLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.OrganizationId == organizationId && l.Id == linkId, cancellationToken);

        if (row is null)
            return null;

        return new JobLinkResponse(
            row.Id,
            row.SourceReportId,
            row.TargetReportId,
            "", "", "", row.LinkType, row.CreatedAt);
    }

    public Task<bool> DeleteLinkAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("links.delete", token => DeleteLinkAsyncCoreAsync(organizationId, linkId, token), cancellationToken);

    private async Task<bool> DeleteLinkAsyncCoreAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            return false;

        var row = await _dbContext.JobReportLinks
            .FirstOrDefaultAsync(l => l.OrganizationId == organizationId && l.Id == linkId, cancellationToken);

        if (row is null)
            return false;

        _dbContext.JobReportLinks.Remove(row);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<IReadOnlyList<JobReportLinkRow>> GetLinkRowsAsync(Guid organizationId, Guid reportId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
