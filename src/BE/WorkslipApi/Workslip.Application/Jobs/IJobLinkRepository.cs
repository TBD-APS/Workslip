using Workslip.Domain.Models;

namespace Workslip.Application.Jobs;

public interface IJobLinkRepository
{
    Task<JobLinkResponse> CreateLinkAsync(Guid organizationId, Guid sourceReportId, Guid targetReportId, CancellationToken cancellationToken);
    Task<JobReportLinkRow?> GetLinkAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken);
    Task<bool> DeleteLinkAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken);
    Task DeleteLinksAsync(Guid organizationId, IReadOnlyList<Guid> linkIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobReportLinkRow>> GetLinkRowsAsync(Guid organizationId, Guid reportId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobLinkInfoResponse>> GetLinkInfoAsync(Guid organizationId, Guid reportId, CancellationToken cancellationToken);
}
