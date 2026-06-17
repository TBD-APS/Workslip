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

    public Task<JobLinkResponse> CreateLinkAsync(Guid organizationId, Guid sourceReportId, Guid targetReportId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("links.create", token => CreateLinkAsyncCoreAsync(organizationId, sourceReportId, targetReportId, token), cancellationToken);

    public Task<IReadOnlyList<JobLinkResponse>> CreateLinksAsync(Guid organizationId, Guid sourceReportId, IReadOnlyList<Guid> targetReportIds, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("links.create-batch", token => CreateLinksAsyncCoreAsync(organizationId, sourceReportId, targetReportIds, token), cancellationToken);

    private async Task<JobLinkResponse> CreateLinkAsyncCoreAsync(Guid organizationId, Guid sourceReportId, Guid targetReportId, CancellationToken cancellationToken)
    {
        var links = await CreateLinksAsyncCoreAsync(organizationId, sourceReportId, [targetReportId], cancellationToken);
        return links.Single();
    }

    private async Task<IReadOnlyList<JobLinkResponse>> CreateLinksAsyncCoreAsync(Guid organizationId, Guid sourceReportId, IReadOnlyList<Guid> targetReportIds, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedTargetIds = targetReportIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (normalizedTargetIds.Length == 0)
            return [];

        var linkedReports = await _dbContext.JobReports
            .AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && normalizedTargetIds.Contains(r.Id))
            .Select(r => new { r.Id, r.ReportNumber, r.Status, r.CustomerId })
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        var links = normalizedTargetIds.Select(targetReportId => new JobReportLinkRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            SourceReportId = sourceReportId,
            TargetReportId = targetReportId,
            CreatedAt = now
        }).ToArray();

        _dbContext.JobReportLinks.AddRange(links);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return links.Select(link =>
        {
            linkedReports.TryGetValue(link.TargetReportId, out var linkedReport);
            return new JobLinkResponse(
                link.Id,
                sourceReportId,
                link.TargetReportId,
                linkedReport?.ReportNumber ?? string.Empty,
                string.Empty,
                linkedReport?.Status ?? string.Empty,
                now);
        }).ToArray();
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

    public Task DeleteLinksAsync(Guid organizationId, IReadOnlyList<Guid> linkIds, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("links.delete-batch", token => DeleteLinksAsyncCoreAsync(organizationId, linkIds, token), cancellationToken);

    private async Task DeleteLinksAsyncCoreAsync(Guid organizationId, IReadOnlyList<Guid> linkIds, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.JobReportLinks
            .Where(l => l.OrganizationId == organizationId && linkIds.Contains(l.Id))
            .ToListAsync(cancellationToken);

        _dbContext.JobReportLinks.RemoveRange(rows);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

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
            join c in _dbContext.Customers.AsNoTracking() on new { Id = r.CustomerId, r.OrganizationId } equals new { Id = (Guid?)c.Id, c.OrganizationId } into rjc
            from c in rjc.DefaultIfEmpty()
            where r.OrganizationId == organizationId && linkedIds.Contains(r.Id)
            select new LinkMapper.LinkedReportInfo(r.Id, r.ReportNumber ?? string.Empty, r.Status, c.Address, c.Name)
        ).ToDictionaryAsync(r => r.Id, cancellationToken);

        return links
            .Select(link => LinkMapper.ToResponse(reportId, link,
                linkedReports.GetValueOrDefault(
                    link.SourceReportId == reportId ? link.TargetReportId : link.SourceReportId)))
            .ToArray();
    }
}
