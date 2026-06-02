using Workslip.Application.Jobs;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Mappers;

public static class LinkMapper
{
    public sealed record LinkedReportInfo(
        Guid Id,
        string ReportNumber,
        string Status,
        string? CustomerName);

    public static JobLinkInfoResponse ToResponse(
        Guid reportId,
        JobReportLinkRow link,
        LinkedReportInfo? linkedReport)
    {
        var linkedId = link.SourceReportId == reportId ? link.TargetReportId : link.SourceReportId;
        return new JobLinkInfoResponse(
            link.Id,
            linkedId,
            linkedReport?.ReportNumber ?? "",
            linkedReport?.CustomerName ?? "",
            linkedReport?.Status ?? "",
            link.LinkType);
    }
}
