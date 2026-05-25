namespace Workslip.Application.Jobs;

public interface IJobLinkRepository
{
    Task<JobLinkResponse> CreateLinkAsync(Guid sourceReportId, Guid targetReportId, string linkType, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobLinkResponse>> GetLinksAsync(Guid reportId, CancellationToken cancellationToken);
    Task<JobLinkResponse?> GetLinkAsync(Guid linkId, CancellationToken cancellationToken);
    Task<bool> DeleteLinkAsync(Guid linkId, CancellationToken cancellationToken);
}
