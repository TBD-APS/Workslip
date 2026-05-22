using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

public enum JobServiceResultStatus
{
    Success,
    ValidationFailed,
    NotFound
}

public sealed record JobServiceResult<T>(
    JobServiceResultStatus Status,
    T? Value,
    IReadOnlyList<JobValidationError> Errors)
{
    public static JobServiceResult<T> Success(T value) => new(JobServiceResultStatus.Success, value, []);
    public static JobServiceResult<T> ValidationFailed(IReadOnlyList<JobValidationError> errors) => new(JobServiceResultStatus.ValidationFailed, default, errors);
    public static JobServiceResult<T> NotFound() => new(JobServiceResultStatus.NotFound, default, []);
}

public interface IJobService
{
    Task<JobServiceResult<JobReportResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobListItemResponse>> ListAsync(Guid? organizationId, JobStatus? status, int? limit, int? offset, CancellationToken cancellationToken);
    Task<JobServiceResult<JobReportResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<JobServiceResult<JobReportResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken);
    Task<JobServiceResult<JobReportResponse>> SubmitAsync(Guid id, CancellationToken cancellationToken);
    Task<JobServiceResult<JobReportResponse>> ApproveAsync(Guid id, Guid? actorId, CancellationToken cancellationToken);
    Task<JobServiceResult<JobReportResponse>> RejectAsync(Guid id, Guid? actorId, CancellationToken cancellationToken);
}

public sealed class JobService(
    IJobRepository repository,
    IJobTaxonomyRepository taxonomyRepository,
    HybridCache cache,
    ILogger<JobService> logger) : IJobService
{
    private static readonly HybridCacheEntryOptions JobReportCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    private static readonly HybridCacheEntryOptions JobListCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(1),
        LocalCacheExpiration = TimeSpan.FromSeconds(15)
    };

    public async Task<JobServiceResult<JobReportResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken)
    {
        var taxonomy = await taxonomyRepository.GetAsync(cancellationToken);
        var errors = JobRequestValidator.ValidateCreate(request, taxonomy);
        if (errors.Count > 0)
        {
            logger.LogWarning("Job create validation failed. OrganizationId: {OrganizationId}. Fields: {ValidationFields}",
                request.OrganizationId,
                ValidationFields(errors));

            return JobServiceResult<JobReportResponse>.ValidationFailed(errors);
        }

        var created = await repository.CreateAsync(request, cancellationToken);
        await InvalidateJobCachesAsync(created.Id, cancellationToken);
        logger.LogInformation(
            "Job created. JobId: {JobId}. OrganizationId: {OrganizationId}. Status: {Status}. ReportNumber: {ReportNumber}. WorkKind: {WorkKind}. InstallationTypeCount: {InstallationTypeCount}. ControlInstallationTypeCount: {ControlInstallationTypeCount}.",
            created.Id,
            created.OrganizationId,
            created.Status,
            created.ReportNumber,
            created.WorkKind,
            created.InstallationTypes.Count,
            created.ControlInstallationTypes.Count);

        return JobServiceResult<JobReportResponse>.Success(created);
    }

    public async Task<IReadOnlyList<JobListItemResponse>> ListAsync(
        Guid? organizationId,
        JobStatus? status,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var query = new JobQuery(organizationId, status, Math.Clamp(limit ?? 50, 1, 200), Math.Max(offset ?? 0, 0));
        var cacheKey = $"jobs:list:organization={query.OrganizationId?.ToString("N") ?? "all"}:status={query.Status?.ToString() ?? "all"}:limit={query.Limit}:offset={query.Offset}";
        var jobs = await cache.GetOrCreateAsync(
            cacheKey,
            async token => (await repository.ListAsync(query, token)).ToArray(),
            JobListCacheOptions,
            tags: ["jobs", "jobs:list"],
            cancellationToken: cancellationToken);

        logger.LogInformation("Jobs listed. OrganizationId: {OrganizationId}. StatusFilter: {StatusFilter}. Limit: {Limit}. Offset: {Offset}. ResultCount: {ResultCount}.",
            query.OrganizationId,
            query.Status,
            query.Limit,
            query.Offset,
            jobs.Length);

        return jobs;
    }

    public async Task<JobServiceResult<JobReportResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var cached = await cache.GetOrCreateAsync(
            JobReportCacheKey(id),
            async token => CachedJobReport.From(await repository.GetAsync(id, token)),
            JobReportCacheOptions,
            tags: ["jobs", JobReportTag(id)],
            cancellationToken: cancellationToken);
        if (!cached.Found || cached.Value is null)
        {
            logger.LogWarning("Job lookup returned not found. JobId: {JobId}.", id);
            return JobServiceResult<JobReportResponse>.NotFound();
        }

        var report = cached.Value;
        logger.LogInformation("Job fetched. JobId: {JobId}. OrganizationId: {OrganizationId}. Status: {Status}.",
            report.Id,
            report.OrganizationId,
            report.Status);

        return JobServiceResult<JobReportResponse>.Success(report);
    }

    public async Task<JobServiceResult<JobReportResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken)
    {
        var taxonomy = await taxonomyRepository.GetAsync(cancellationToken);
        var errors = JobRequestValidator.ValidateUpdate(request, taxonomy);
        if (errors.Count > 0)
        {
            logger.LogWarning("Job update validation failed. JobId: {JobId}. Fields: {ValidationFields}",
                id,
                ValidationFields(errors));

            return JobServiceResult<JobReportResponse>.ValidationFailed(errors);
        }

        var updated = await repository.UpdateAsync(id, request, cancellationToken);
        if (updated is null)
        {
            logger.LogWarning("Job update returned not found. JobId: {JobId}.", id);
            return JobServiceResult<JobReportResponse>.NotFound();
        }

        await InvalidateJobCachesAsync(id, cancellationToken);
        logger.LogInformation(
            "Job updated. JobId: {JobId}. OrganizationId: {OrganizationId}. Status: {Status}. ReportNumber: {ReportNumber}. WorkKind: {WorkKind}. InstallationTypeCount: {InstallationTypeCount}. ControlInstallationTypeCount: {ControlInstallationTypeCount}.",
            updated.Id,
            updated.OrganizationId,
            updated.Status,
            updated.ReportNumber,
            updated.WorkKind,
            updated.InstallationTypes.Count,
            updated.ControlInstallationTypes.Count);

        return JobServiceResult<JobReportResponse>.Success(updated);
    }

    public Task<JobServiceResult<JobReportResponse>> SubmitAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, JobStatus.Submitted, actorId: null, cancellationToken);

    public Task<JobServiceResult<JobReportResponse>> ApproveAsync(Guid id, Guid? actorId, CancellationToken cancellationToken) =>
        TransitionAsync(id, JobStatus.Approved, actorId, cancellationToken);

    public Task<JobServiceResult<JobReportResponse>> RejectAsync(Guid id, Guid? actorId, CancellationToken cancellationToken) =>
        TransitionAsync(id, JobStatus.Rejected, actorId, cancellationToken);

    private async Task<JobServiceResult<JobReportResponse>> TransitionAsync(
        Guid id,
        JobStatus targetStatus,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var report = await repository.TransitionAsync(id, targetStatus, actorId, cancellationToken);
        if (report is null)
        {
            logger.LogWarning("Job transition returned not found. JobId: {JobId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
                id,
                targetStatus,
                actorId);

            return JobServiceResult<JobReportResponse>.NotFound();
        }

        await InvalidateJobCachesAsync(id, cancellationToken);
        logger.LogInformation("Job transitioned. JobId: {JobId}. OrganizationId: {OrganizationId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
            report.Id,
            report.OrganizationId,
            targetStatus,
            actorId);

        return JobServiceResult<JobReportResponse>.Success(report);
    }

    private async Task InvalidateJobCachesAsync(Guid id, CancellationToken cancellationToken)
    {
        await cache.RemoveByTagAsync("jobs:list", cancellationToken);
        await cache.RemoveByTagAsync(JobReportTag(id), cancellationToken);
    }

    private static string JobReportCacheKey(Guid id) => $"jobs:detail:{id:N}";

    private static string JobReportTag(Guid id) => $"jobs:{id:N}";

    private static string ValidationFields(IEnumerable<JobValidationError> errors) =>
        string.Join(",", errors.Select(error => error.Field).Distinct());

    private sealed record CachedJobReport(bool Found, JobReportResponse? Value)
    {
        public static CachedJobReport From(JobReportResponse? value) => new(value is not null, value);
    }
}
