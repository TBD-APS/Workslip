using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Mappers;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfJobLinkRepository : IJobLinkRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;

    public EfJobLinkRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
    }

    public Task<JobLinkResponse> CreateLinkAsync(Guid organizationId, Guid sourceReportId, Guid targetReportId, string linkType, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("links.create", token => CreateLinkAsyncCoreAsync(organizationId, sourceReportId, targetReportId, linkType, token), cancellationToken);

    private async Task<JobLinkResponse> CreateLinkAsyncCoreAsync(Guid organizationId, Guid sourceReportId, Guid targetReportId, string linkType, CancellationToken cancellationToken)
    {
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

    public Task<JobReportLinkRow?> GetLinkAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("links.get", token => GetLinkAsyncCoreAsync(organizationId, linkId, token), cancellationToken);

    private async Task<JobReportLinkRow?> GetLinkAsyncCoreAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.JobReportLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.OrganizationId == organizationId && l.Id == linkId, cancellationToken);

        return row;
    }

    public Task<bool> DeleteLinkAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("links.delete", token => DeleteLinkAsyncCoreAsync(organizationId, linkId, token), cancellationToken);

    private async Task<bool> DeleteLinkAsyncCoreAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.JobReportLinks
        .FirstOrDefaultAsync(l => l.OrganizationId == organizationId && l.Id == linkId, cancellationToken);

        if (row is null)
            return false;

        _dbContext.JobReportLinks.Remove(row);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<JobReportLinkRow>> GetLinkRowsAsync(Guid organizationId, Guid reportId, CancellationToken cancellationToken)
    {
        var links = await _dbContext.JobReportLinks
            .AsNoTracking()
            .Where(l => l.OrganizationId == organizationId && (l.SourceReportId == reportId || l.TargetReportId == reportId))
            .Select(l => new JobReportLinkRow
            {
                Id = l.Id,
                OrganizationId = l.OrganizationId,
                SourceReportId = l.SourceReportId,
                TargetReportId = l.TargetReportId,
                LinkType = l.LinkType,
                CreatedAt = l.CreatedAt
            }).ToListAsync();

        return links;
    }

    public Task<IReadOnlyList<JobLinkInfoResponse>> GetLinkInfoAsync(Guid organizationId, Guid reportId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("links.info", token => GetLinkInfoAsyncCoreAsync(organizationId, reportId, token), cancellationToken);

    private async Task<IReadOnlyList<JobLinkInfoResponse>> GetLinkInfoAsyncCoreAsync(Guid organizationId, Guid reportId, CancellationToken cancellationToken)
    {
        var links = await GetLinkRowsAsync(organizationId, reportId, cancellationToken);

        var linkedIds = links
            .Select(l => l.SourceReportId == reportId ? l.TargetReportId : l.SourceReportId)
            .Distinct()
            .ToArray();

        if (linkedIds.Length == 0) return [];

        var linkedReports = await (
            from r in _dbContext.JobReports.AsNoTracking()
            join c in _dbContext.Customers.AsNoTracking() on new { Id = (Guid?)r.CustomerId, r.OrganizationId } equals new { Id = (Guid?)c.Id, c.OrganizationId } into rjc
            from c in rjc.DefaultIfEmpty()
            where r.OrganizationId == organizationId && linkedIds.Contains(r.Id)
            select new LinkMapper.LinkedReportInfo(r.Id, r.ReportNumber ?? "", r.Status, c != null ? c.Name : null)
        ).ToDictionaryAsync(r => r.Id, cancellationToken);

        return links
            .Select(link => LinkMapper.ToResponse(reportId, link,
                linkedReports.GetValueOrDefault(
                    link.SourceReportId == reportId ? link.TargetReportId : link.SourceReportId)))
            .ToArray();
    }
}
