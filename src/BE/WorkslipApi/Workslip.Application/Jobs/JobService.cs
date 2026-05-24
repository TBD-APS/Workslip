using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

public interface IJobService
{
    Task<Result<JobReportResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobListItemResponse>> ListAsync(Guid? organizationId, JobStatus? status, int? limit, int? offset, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> SubmitAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> ApproveAsync(Guid id, Guid? actorId, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> RejectAsync(Guid id, Guid? actorId, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> AssignAsync(Guid jobId, Guid? userId, CancellationToken cancellationToken);
    Task<Result<JobLinkResponse>> CreateLinkAsync(Guid reportId, CreateJobLinkRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<JobLinkResponse>>> GetLinksAsync(Guid reportId, CancellationToken cancellationToken);
    Task<Result> DeleteLinkAsync(Guid reportId, Guid linkId, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class JobService(
    IJobRepository repository,
    IJobLinkRepository linkRepository,
    HybridCache cache,
    IValidator<CreateJobRequest> createJobValidator,
    IValidator<UpdateJobRequest> updateJobValidator,
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

    public async Task<Result<JobReportResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await createJobValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .Select(e => new ValidationError
                {
                    Identifier = e.PropertyName,
                    ErrorMessage = e.ErrorMessage
                })
                .ToList();
            logger.LogWarning("Job create validation failed. OrganizationId: {OrganizationId}. Fields: {ValidationFields}",
                request.OrganizationId,
                ValidationFields(errors));

            return Result<JobReportResponse>.Invalid(errors);
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

        return Result<JobReportResponse>.Success(created);
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

    public async Task<Result<JobReportResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
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
            return Result<JobReportResponse>.NotFound();
        }

        var report = cached.Value;
        logger.LogInformation("Job fetched. JobId: {JobId}. OrganizationId: {OrganizationId}. Status: {Status}.",
            report.Id,
            report.OrganizationId,
            report.Status);

        return Result<JobReportResponse>.Success(report);
    }

    public async Task<Result<JobReportResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await updateJobValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .Select(e => new ValidationError
                {
                    Identifier = e.PropertyName,
                    ErrorMessage = e.ErrorMessage
                })
                .ToList();
            logger.LogWarning("Job update validation failed. JobId: {JobId}. Fields: {ValidationFields}",
                id,
                ValidationFields(errors));

            return Result<JobReportResponse>.Invalid(errors);
        }

        var updated = await repository.UpdateAsync(id, request, cancellationToken);
        if (updated is null)
        {
            logger.LogWarning("Job update returned not found. JobId: {JobId}.", id);
            return Result<JobReportResponse>.NotFound();
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

        return Result<JobReportResponse>.Success(updated);
    }

    public Task<Result<JobReportResponse>> SubmitAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, JobStatus.Submitted, actorId: null, cancellationToken);

    public Task<Result<JobReportResponse>> ApproveAsync(Guid id, Guid? actorId, CancellationToken cancellationToken) =>
        TransitionAsync(id, JobStatus.Approved, actorId, cancellationToken);

    public Task<Result<JobReportResponse>> RejectAsync(Guid id, Guid? actorId, CancellationToken cancellationToken) =>
        TransitionAsync(id, JobStatus.Rejected, actorId, cancellationToken);

    private async Task<Result<JobReportResponse>> TransitionAsync(
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

            return Result<JobReportResponse>.NotFound();
        }

        await InvalidateJobCachesAsync(id, cancellationToken);
        logger.LogInformation("Job transitioned. JobId: {JobId}. OrganizationId: {OrganizationId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
            report.Id,
            report.OrganizationId,
            targetStatus,
            actorId);

        return Result<JobReportResponse>.Success(report);
    }

    public async Task<Result<JobLinkResponse>> CreateLinkAsync(Guid reportId, CreateJobLinkRequest request, CancellationToken cancellationToken)
    {
        if (reportId == request.TargetReportId)
        {
            return Result<JobLinkResponse>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "TargetReportId", ErrorMessage = "En sag kan ikke linkes til sig selv." }
            });
        }

        var report = await repository.GetAsync(reportId, cancellationToken);
        if (report is null)
        {
            return Result<JobLinkResponse>.NotFound();
        }

        var target = await repository.GetAsync(request.TargetReportId, cancellationToken);
        if (target is null)
        {
            return Result<JobLinkResponse>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "TargetReportId", ErrorMessage = "Den valgte sag findes ikke." }
            });
        }

        if (report.OrganizationId != target.OrganizationId)
        {
            return Result<JobLinkResponse>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "TargetReportId", ErrorMessage = "Kunne ikke finde den sag du referer til." }
            });
        }

        var link = await linkRepository.CreateLinkAsync(reportId, request.TargetReportId, request.LinkType, cancellationToken);
        logger.LogInformation("Job link created. SourceReportId: {SourceReportId}. TargetReportId: {TargetReportId}. LinkType: {LinkType}.",
            reportId, request.TargetReportId, request.LinkType);

        return Result<JobLinkResponse>.Success(link);
    }

    public async Task<Result<IReadOnlyList<JobLinkResponse>>> GetLinksAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var report = await repository.GetAsync(reportId, cancellationToken);
        if (report is null)
        {
            return Result<IReadOnlyList<JobLinkResponse>>.NotFound();
        }

        var links = await linkRepository.GetLinksAsync(reportId, cancellationToken);
        return Result<IReadOnlyList<JobLinkResponse>>.Success(links);
    }

    public async Task<Result> DeleteLinkAsync(Guid reportId, Guid linkId, CancellationToken cancellationToken)
    {
        var report = await repository.GetAsync(reportId, cancellationToken);
        if (report is null)
        {
            return Result.NotFound();
        }

        var link = await linkRepository.GetLinkAsync(linkId, cancellationToken);
        if (link is null)
        {
            return Result.NotFound();
        }

        var deleted = await linkRepository.DeleteLinkAsync(linkId, cancellationToken);
        if (!deleted)
        {
            return Result.NotFound();
        }

        logger.LogInformation("Job link deleted. LinkId: {LinkId}. ReportId: {ReportId}.", linkId, reportId);
        return Result.Success();
    }

     public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
     {
         var deleted = await repository.DeleteAsync(id, cancellationToken);
         if (!deleted)
         {
             logger.LogWarning("Job delete returned not found. JobId: {JobId}.", id);
             return Result.NotFound();
         }

         await InvalidateJobCachesAsync(id, cancellationToken);
         logger.LogInformation("Job deleted. JobId: {JobId}.", id);

         return Result.NoContent();
     }

     public async Task<Result<JobReportResponse>> AssignAsync(Guid jobId, Guid? userId, CancellationToken cancellationToken)
     {
         var assigned = await repository.AssignAsync(jobId, userId, actorId: null, cancellationToken);
         if (assigned is null)
         {
             return Result<JobReportResponse>.NotFound(); 
         }

         await InvalidateJobCachesAsync(jobId, cancellationToken);
         logger.LogInformation("Job assigned. JobId: {JobId}. AssignedUserId: {AssignedUserId}.", jobId, userId);

         return Result<JobReportResponse>.Success(assigned);
     }

    private async Task InvalidateJobCachesAsync(Guid id, CancellationToken cancellationToken)
    {
        await cache.RemoveByTagAsync("jobs:list", cancellationToken);
        await cache.RemoveByTagAsync(JobReportTag(id), cancellationToken);
    }

    private static string JobReportCacheKey(Guid id) => $"jobs:detail:{id:N}";

    private static string JobReportTag(Guid id) => $"jobs:{id:N}";

    private static string ValidationFields(IEnumerable<ValidationError> errors) =>
        string.Join(",", errors.Select(error => error.Identifier).Distinct());

    private sealed record CachedJobReport(bool Found, JobReportResponse? Value)
    {
        public static CachedJobReport From(JobReportResponse? value) => new(value is not null, value);
    }
}
