using Dapper;
using Workslip.Application.Jobs;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Repositories;

public sealed class DapperJobLinkRepository(ISqlConnectionFactory connectionFactory, IDatabaseRetryPolicy retryPolicy) : IJobLinkRepository
{
    public Task<JobLinkResponse> CreateLinkAsync(Guid organizationId, Guid sourceReportId, Guid targetReportId, string linkType, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("links.create", token => CreateLinkAsyncCoreAsync(organizationId, sourceReportId, targetReportId, linkType, token), cancellationToken);

    private async Task<JobLinkResponse> CreateLinkAsyncCoreAsync(Guid organizationId, Guid sourceReportId, Guid targetReportId, string linkType, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var linkId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var normalisedSource = sourceReportId.CompareTo(targetReportId) < 0 ? sourceReportId : targetReportId;
        var normalisedTarget = sourceReportId.CompareTo(targetReportId) < 0 ? targetReportId : sourceReportId;

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into dbo.JobReportLinks (Id, OrganizationId, SourceReportId, TargetReportId, LinkType, CreatedAt)
            values (@Id, @OrganizationId, @SourceReportId, @TargetReportId, @LinkType, @CreatedAt);
            """,
            new
            {
                Id = linkId,
                OrganizationId = organizationId,
                SourceReportId = normalisedSource,
                TargetReportId = normalisedTarget,
                LinkType = linkType,
                CreatedAt = now
            },
            cancellationToken: cancellationToken));

        var linked = await connection.QuerySingleAsync<JobReportRow>(new CommandDefinition(
            "select * from dbo.JobReports where Id = @Id and OrganizationId = @OrganizationId;",
            new { Id = targetReportId, OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        return new JobLinkResponse(
            linkId,
            sourceReportId,
            targetReportId,
            linked.ReportNumber ?? string.Empty,
            linked.CustomerName ?? string.Empty,
            linked.Status,
            linkType,
            now);
    }

    public Task<IReadOnlyList<JobLinkResponse>> GetLinksAsync(Guid organizationId, Guid reportId, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("links.list", token => GetLinksAsyncCoreAsync(organizationId, reportId, token), cancellationToken);

    private async Task<IReadOnlyList<JobLinkResponse>> GetLinksAsyncCoreAsync(Guid organizationId, Guid reportId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var links = await connection.QueryAsync<JobReportLinkRow>(new CommandDefinition(
            "select * from dbo.JobReportLinks where OrganizationId = @OrganizationId and (SourceReportId = @Id or TargetReportId = @Id);",
            new { Id = reportId, OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        var linkedIds = links.Select(link =>
            link.SourceReportId == reportId ? link.TargetReportId : link.SourceReportId).Distinct().ToArray();

        var linkedReports = linkedIds.Length == 0
            ? []
            : (await connection.QueryAsync<JobReportRow>(new CommandDefinition(
                "select * from dbo.JobReports where OrganizationId = @OrganizationId and Id in @Ids;",
                new { Ids = linkedIds, OrganizationId = organizationId },
                cancellationToken: cancellationToken)))
                .ToDictionary(r => r.Id);

        return links.Select(link =>
        {
            var linkedId = link.SourceReportId == reportId ? link.TargetReportId : link.SourceReportId;
            var linked = linkedReports.GetValueOrDefault(linkedId);
            return new JobLinkResponse(
                link.Id,
                reportId,
                linkedId,
                linked?.ReportNumber ?? "",
                linked?.CustomerName ?? "",
                linked?.Status ?? "",
                link.LinkType,
                link.CreatedAt);
        }).ToArray();
    }

    public Task<JobLinkResponse?> GetLinkAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("links.get", token => GetLinkAsyncCoreAsync(organizationId, linkId, token), cancellationToken);

    private async Task<JobLinkResponse?> GetLinkAsyncCoreAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<JobReportLinkRow>(new CommandDefinition(
            "select * from dbo.JobReportLinks where Id = @Id and OrganizationId = @OrganizationId;",
            new { Id = linkId, OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        if (row is null)
            return null;

        return new JobLinkResponse(
            row.Id,
            row.SourceReportId,
            row.TargetReportId,
            "", "", "", row.LinkType, row.CreatedAt);
    }

    public Task<bool> DeleteLinkAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("links.delete", token => DeleteLinkAsyncCoreAsync(organizationId, linkId, token), cancellationToken);

    private async Task<bool> DeleteLinkAsyncCoreAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "delete from dbo.JobReportLinks where Id = @Id and OrganizationId = @OrganizationId;",
            new { Id = linkId, OrganizationId = organizationId },
            cancellationToken: cancellationToken));

        return affected > 0;
    }
}
