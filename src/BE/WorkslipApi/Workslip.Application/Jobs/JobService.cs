using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Application.Jobs;

public interface IJobService
{
    Task<Result<JobReportResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobListItemResponse>> ListAsync(Guid? organizationId, JobStatus? status, int? limit, int? offset, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<JobReportSummaryResponse>> GetReportSummaryAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<JobEventResponse>>> GetHistoryAsync(Guid id, int? limit, int? offset, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> SubmitAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> ApproveAsync(Guid id, Guid? actorId, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> RejectAsync(Guid id, Guid? actorId, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> AssignAsync(Guid jobId, Guid? userId, CancellationToken cancellationToken);
    Task<Result<JobLinkResponse>> CreateLinkAsync(Guid reportId, CreateJobLinkRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<JobLinkResponse>>> GetLinksAsync(Guid reportId, CancellationToken cancellationToken);
    Task<Result> DeleteLinkAsync(Guid reportId, Guid linkId, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> RestoreDeletionAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class JobService(
    IJobRepository repository,
    IJobLinkRepository linkRepository,
    IJobTaxonomyRepository taxonomyRepository,
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

    public async Task<Result<JobReportSummaryResponse>> GetReportSummaryAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await GetAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            return Result<JobReportSummaryResponse>.NotFound();
        }

        var taxonomy = await taxonomyRepository.GetAsync(cancellationToken);
        var summary = ToSummary(result.Value, taxonomy);

        logger.LogInformation("Job report summary fetched. JobId: {JobId}. OrganizationId: {OrganizationId}. Status: {Status}.",
            summary.Id,
            summary.OrganizationId,
            summary.Status);

        return Result<JobReportSummaryResponse>.Success(summary);
    }

    public async Task<Result<IReadOnlyList<JobEventResponse>>> GetHistoryAsync(
        Guid id,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var normalizedOffset = Math.Max(offset ?? 0, 0);
        var events = await repository.GetEventsAsync(id, normalizedLimit, normalizedOffset, cancellationToken);
        if (events is null)
        {
            logger.LogWarning("Job history lookup returned not found. JobId: {JobId}.", id);
            return Result<IReadOnlyList<JobEventResponse>>.NotFound();
        }

        logger.LogInformation("Job history fetched. JobId: {JobId}. Limit: {Limit}. Offset: {Offset}. EventCount: {EventCount}.",
            id,
            normalizedLimit,
            normalizedOffset,
            events.Count);

        return Result<IReadOnlyList<JobEventResponse>>.Success(events);
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

    public async Task<Result<JobReportResponse>> SubmitAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, cancellationToken);
        if (current is null)
        {
            logger.LogWarning("Job submit returned not found. JobId: {JobId}.", id);
            return Result<JobReportResponse>.NotFound();
        }

        var taxonomy = await taxonomyRepository.GetAsync(cancellationToken);
        var validationErrors = ValidateReadyForSubmission(current, taxonomy);
        if (validationErrors.Count != 0)
        {
            logger.LogWarning("Job submit validation failed. JobId: {JobId}. Fields: {ValidationFields}",
                id,
                ValidationFields(validationErrors));

            return Result<JobReportResponse>.Invalid(validationErrors);
        }

        return await TransitionAsync(id, JobStatus.Submitted, actorId: null, cancellationToken);
    }

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
         logger.LogInformation("Job deletion scheduled. JobId: {JobId}.", id);

         return Result.NoContent();
     }

     public async Task<Result<JobReportResponse>> RestoreDeletionAsync(Guid id, CancellationToken cancellationToken)
     {
         var restored = await repository.RestoreDeletionAsync(id, cancellationToken);
         if (restored is null)
         {
             logger.LogWarning("Job restore deletion returned not found. JobId: {JobId}.", id);
             return Result<JobReportResponse>.NotFound();
         }

         await InvalidateJobCachesAsync(id, cancellationToken);
         logger.LogInformation("Job deletion restored. JobId: {JobId}.", id);

         return Result<JobReportResponse>.Success(restored);
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

    private static JobReportSummaryResponse ToSummary(JobReportResponse report, JobTaxonomySnapshot taxonomy)
    {
        var workKindLabel = !string.IsNullOrWhiteSpace(report.WorkKind) && taxonomy.WorkKinds.TryGetValue(report.WorkKind, out var workKind)
            ? workKind.Label
            : report.WorkKind;

        var closureFlags = report.ClosureFlags
            .Select(flagId => new JobReportSummaryClosureFlagResponse(
                flagId,
                taxonomy.ClosureFlags.TryGetValue(flagId, out var flag) ? flag.Label : flagId))
            .ToArray();

        return new(
            report.Id,
            report.OrganizationId,
            report.ReportNumber,
            report.Status,
            new JobReportSummaryCustomerResponse(
                report.CustomerId,
                report.CustomerName,
                report.CustomerAddress,
                report.CustomerEmail,
                report.ContactPerson,
                report.Phone),
            new JobReportSummaryWorkResponse(
                report.WorkKind,
                workKindLabel,
                report.CustomWorkKind,
                report.InstallationTypes,
                closureFlags,
                report.Remarks),
            new JobReportSummaryObservationResponse(
                report.ReportDate,
                report.TaskDescription,
                report.CustomerObservations,
                report.TechnicalObservations,
                report.Payload),
            report.ControlInstallationTypes,
            report.Links,
            report.CreatedAt,
            report.UpdatedAt,
            report.SubmittedAt,
            report.AssignedUser,
            report.DeletionScheduledAt);
    }

    private static List<ValidationError> ValidateReadyForSubmission(JobReportResponse report, JobTaxonomySnapshot taxonomy)
    {
        var errors = new List<ValidationError>();
        AddRequired(errors, nameof(JobReportResponse.ReportNumber), report.ReportNumber, "Report number is required.");
        AddRequired(errors, nameof(JobReportResponse.CustomerName), report.CustomerName, "Customer name is required.");
        AddRequired(errors, nameof(JobReportResponse.CustomerAddress), report.CustomerAddress, "Customer address is required.");
        AddRequired(errors, nameof(JobReportResponse.TaskDescription), report.TaskDescription, "Task description is required.");

        if (report.InstallationTypes.Count == 0)
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.InstallationTypes), ErrorMessage = "Select at least one installation type." });
        }

        if (string.IsNullOrWhiteSpace(report.WorkKind))
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = "Work kind is required." });
        }
        else if (!taxonomy.WorkKinds.TryGetValue(report.WorkKind, out var workKind))
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = $"Unknown work kind '{report.WorkKind}'." });
        }
        else if (workKind.RequiresCustomWorkKind && string.IsNullOrWhiteSpace(report.CustomWorkKind))
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.CustomWorkKind), ErrorMessage = "Custom work kind is required for this work kind." });
        }
        else if (!workKind.RequiresCustomWorkKind && !string.IsNullOrWhiteSpace(report.CustomWorkKind))
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.CustomWorkKind), ErrorMessage = "Custom work kind is only allowed for work kinds that require custom text." });
        }

        return errors;
    }

    private static void AddRequired(List<ValidationError> errors, string identifier, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationError { Identifier = identifier, ErrorMessage = message });
        }
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
