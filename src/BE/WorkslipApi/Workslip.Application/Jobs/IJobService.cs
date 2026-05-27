using Ardalis.Result;
using Workslip.Domain;

namespace Workslip.Application.Jobs
{
    public interface IJobService
    {
        Task<Result<JobReportResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken);
        Task<Result<IReadOnlyList<JobListItemResponse>>> ListAsync(JobStatus? status, int? limit, int? offset, CancellationToken cancellationToken);
        Task<Result<JobReportResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
        Task<Result<JobReportSummaryResponse>> GetReportSummaryAsync(Guid id, CancellationToken cancellationToken);
        Task<Result<IReadOnlyList<JobEventResponse>>> GetHistoryAsync(Guid id, int? limit, int? offset, CancellationToken cancellationToken);
        Task<Result<JobReportResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken);
        Task<Result<JobReportResponse>> ChangeStatusAsync(Guid id, ChangeJobStatusRequest request, CancellationToken cancellationToken);
        Task<Result<JobReportResponse>> AssignAsync(Guid jobId, IReadOnlyList<Guid>? userIds, CancellationToken cancellationToken);
        Task<Result<JobLinkResponse>> CreateLinkAsync(Guid reportId, CreateJobLinkRequest request, CancellationToken cancellationToken);
        Task<Result<IReadOnlyList<JobLinkResponse>>> GetLinksAsync(Guid reportId, CancellationToken cancellationToken);
        Task<Result> DeleteLinkAsync(Guid reportId, Guid linkId, CancellationToken cancellationToken);
        Task<Result<JobReportResponse>> DeleteAsync(Guid id, CancellationToken cancellationToken);
        Task<Result<JobReportResponse>> RestoreDeletionAsync(Guid id, CancellationToken cancellationToken);
    }

}
