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

    public Task<IReadOnlyList<JobLinkResponse>> GetLinksAsync(Guid organizationId, Guid reportId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("links.list", token => GetLinksAsyncCoreAsync(organizationId, reportId, token), cancellationToken);

    private async Task<IReadOnlyList<JobLinkResponse>> GetLinksAsyncCoreAsync(Guid organizationId, Guid reportId, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            return [];

        var links = await _dbContext.JobReportLinks
            .AsNoTracking()
            .Where(l => l.OrganizationId == organizationId && (l.SourceReportId == reportId || l.TargetReportId == reportId))
            .ToListAsync(cancellationToken);

        var linkedIds = links
            .Select(l => l.SourceReportId == reportId ? l.TargetReportId : l.SourceReportId)
            .Distinct()
            .ToArray();

        var linkedReports = linkedIds.Length == 0
            ? []
            : await _dbContext.JobReports
                .AsNoTracking()
                .Where(r => r.OrganizationId == organizationId && linkedIds.Contains(r.Id))
                .Select(r => new { r.Id, r.ReportNumber, r.Status, r.CustomerId })
                .ToDictionaryAsync(r => r.Id, cancellationToken);

        var customerIds = linkedReports.Values
            .Select(r => r.CustomerId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        var customerNames = customerIds.Length == 0
            ? []
            : await _dbContext.Customers
                .AsNoTracking()
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return links.Select(link =>
        {
            var linkedId = link.SourceReportId == reportId ? link.TargetReportId : link.SourceReportId;
            var linked = linkedReports.GetValueOrDefault(linkedId);
            var name = linked?.CustomerId is not null
                ? customerNames.GetValueOrDefault(linked.CustomerId.Value) ?? string.Empty : string.Empty;

            return new JobLinkResponse(
                link.Id,
                reportId,
                linkedId,
                linked?.ReportNumber ?? string.Empty,
                name,
                linked?.Status ?? string.Empty,
                link.LinkType,
                link.CreatedAt);
        }).ToArray();
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
}
