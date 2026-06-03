using Ardalis.Result;
using Workslip.Domain;

namespace Workslip.Application.Jobs
{
    public interface IJobService
    {
        Task<Result<JobReportSummaryResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken);
        Task<Result<IReadOnlyList<JobListItemResponse>>> ListAsync(JobStatus? status, string? reportNumber, string? customerName, string? customerEmail, string? customerAddress, int? limit, int? offset, CancellationToken cancellationToken);
        Task<Result<IReadOnlyList<JobListItemResponse>>> GetMyAssignedJobsAsync(CancellationToken cancellationToken);
        Task<Result<JobReportSummaryResponse>> GetSingleJobAsync(Guid id, CancellationToken cancellationToken);
        Task<Result<IReadOnlyList<JobEventResponse>>> GetHistoryAsync(Guid id, int? limit, int? offset, CancellationToken cancellationToken);
        Task<Result<JobReportSummaryResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken);
        Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(Guid id, ChangeJobStatusRequest request, CancellationToken cancellationToken);
        Task<Result<JobReportSummaryResponse>> AssignAsync(Guid jobId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken);
        Task<Result<IReadOnlyList<JobLinkResponse>>> CreateLinksAsync(Guid reportId, CreateJobLinkRequest request, CancellationToken cancellationToken);
        Task<Result> DeleteLinksAsync(Guid reportId, DeleteJobLinksRequest request, CancellationToken cancellationToken);
        Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
        Task<Result<JobReportSummaryResponse>> RestoreDeletionAsync(Guid id, CancellationToken cancellationToken);
    }

}
