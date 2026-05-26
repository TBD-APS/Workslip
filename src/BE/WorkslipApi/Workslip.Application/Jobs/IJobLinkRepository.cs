namespace Workslip.Application.Jobs;

public interface IJobLinkRepository
{
    Task<JobLinkResponse> CreateLinkAsync(Guid organizationId, Guid sourceReportId, Guid targetReportId, string linkType, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobLinkResponse>> GetLinksAsync(Guid organizationId, Guid reportId, CancellationToken cancellationToken);
    Task<JobLinkResponse?> GetLinkAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken);
    Task<bool> DeleteLinkAsync(Guid organizationId, Guid linkId, CancellationToken cancellationToken);
}
