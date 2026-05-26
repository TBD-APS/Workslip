using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Application.Jobs;

public interface IJobService
{
    Task<Result<JobReportResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<JobListItemResponse>>> ListAsync(JobStatus? status, int? limit, int? offset, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<JobReportSummaryResponse>> GetReportSummaryAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<JobEventResponse>>> GetHistoryAsync(Guid id, int? limit, int? offset, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> SubmitAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> ApproveAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> RejectAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> AssignAsync(Guid jobId, Guid? userId, CancellationToken cancellationToken);
    Task<Result<JobLinkResponse>> CreateLinkAsync(Guid reportId, CreateJobLinkRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<JobLinkResponse>>> GetLinksAsync(Guid reportId, CancellationToken cancellationToken);
    Task<Result> DeleteLinkAsync(Guid reportId, Guid linkId, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<JobReportResponse>> RestoreDeletionAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class JobService(
    IJobRepository repository,
    IJobLinkRepository linkRepository,
    IJobTaxonomyRepository taxonomyRepository,
    HybridCache cache,
    IValidator<CreateJobRequest> createJobValidator,
    IValidator<UpdateJobRequest> updateJobValidator,
    ICurrentUserContext currentUser,
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
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportResponse>.Unauthorized();
        }

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
                organizationId.Value,
                ValidationFields(errors));

            return Result<JobReportResponse>.Invalid(errors);
        }

        var taxonomyErrors = await ValidateDraftTaxonomyAsync(
            request.WorkKind,
            request.CustomWorkKind,
            request.ClosureFlags,
            cancellationToken);
        if (taxonomyErrors.Count != 0)
        {
            logger.LogWarning("Job create taxonomy validation failed. OrganizationId: {OrganizationId}. Fields: {ValidationFields}",
                organizationId.Value,
                ValidationFields(taxonomyErrors));

            return Result<JobReportResponse>.Invalid(taxonomyErrors);
        }

        var created = await repository.CreateAsync(organizationId.Value, request, cancellationToken);
        await InvalidateJobCachesAsync(created.Id, created.OrganizationId, cancellationToken);
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

    public async Task<Result<IReadOnlyList<JobListItemResponse>>> ListAsync(
        JobStatus? status,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<IReadOnlyList<JobListItemResponse>>.Unauthorized();
        }

        var query = new JobQuery(organizationId.Value, status, Math.Clamp(limit ?? 50, 1, 200), Math.Max(offset ?? 0, 0));
        var cacheKey = $"jobs:list:organization={query.OrganizationId:N}:status={query.Status?.ToString() ?? "all"}:limit={query.Limit}:offset={query.Offset}";
        
        var jobs = await cache.GetOrCreateAsync(
            cacheKey,
            async token => (await repository.ListAsync(query, token)).ToArray(),
            JobListCacheOptions,
            tags: ["jobs", JobListTag(query.OrganizationId)],
            cancellationToken: cancellationToken);

        logger.LogInformation("Jobs listed. OrganizationId: {OrganizationId}. StatusFilter: {StatusFilter}. Limit: {Limit}. Offset: {Offset}. ResultCount: {ResultCount}.",
            query.OrganizationId,
            query.Status,
            query.Limit,
            query.Offset,
            jobs.Length);

        return Result<IReadOnlyList<JobListItemResponse>>.Success(jobs);
    }

    public async Task<Result<JobReportResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportResponse>.Unauthorized();
        }

        var cached = await cache.GetOrCreateAsync(
            JobReportCacheKey(id, organizationId.Value),
            async token => CachedJobReport.From(await repository.GetAsync(id, organizationId.Value, token)),
            JobReportCacheOptions,
            tags: ["jobs", JobReportTag(id, organizationId.Value)],
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
            return result.Status == ResultStatus.Unauthorized
                ? Result<JobReportSummaryResponse>.Unauthorized()
                : Result<JobReportSummaryResponse>.NotFound();
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
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<IReadOnlyList<JobEventResponse>>.Unauthorized();
        }

        var normalizedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var normalizedOffset = Math.Max(offset ?? 0, 0);
        var events = await repository.GetEventsAsync(id, organizationId.Value, normalizedLimit, normalizedOffset, cancellationToken);
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

        var taxonomyErrors = await ValidateDraftTaxonomyAsync(
            request.WorkKind,
            request.CustomWorkKind,
            request.ClosureFlags,
            cancellationToken);
        if (taxonomyErrors.Count != 0)
        {
            logger.LogWarning("Job update taxonomy validation failed. JobId: {JobId}. Fields: {ValidationFields}",
                id,
                ValidationFields(taxonomyErrors));

            return Result<JobReportResponse>.Invalid(taxonomyErrors);
        }

        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportResponse>.Unauthorized();
        }

        var updated = await repository.UpdateAsync(id, organizationId.Value, request, cancellationToken);
        if (updated is null)
        {
            logger.LogWarning("Job update returned not found. JobId: {JobId}.", id);
            return Result<JobReportResponse>.NotFound();
        }

        await InvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
        
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
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportResponse>.Unauthorized();
        }

        var current = await repository.GetAsync(id, organizationId.Value, cancellationToken);
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

        return await TransitionAsync(id, JobStatus.Submitted, cancellationToken);
    }

    public Task<Result<JobReportResponse>> ApproveAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, JobStatus.Approved, cancellationToken);

    public Task<Result<JobReportResponse>> RejectAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, JobStatus.Rejected, cancellationToken);

    private async Task<Result<JobReportResponse>> TransitionAsync(
        Guid id,
        JobStatus targetStatus,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportResponse>.Unauthorized();
        }

        var actorId = currentUser.UserId;
        var report = await repository.TransitionAsync(id, organizationId.Value, targetStatus, actorId, cancellationToken);
        if (report is null)
        {
            logger.LogWarning("Job transition returned not found. JobId: {JobId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
                id,
                targetStatus,
                actorId);

            return Result<JobReportResponse>.NotFound();
        }

        await InvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
        logger.LogInformation("Job transitioned. JobId: {JobId}. OrganizationId: {OrganizationId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
            report.Id,
            report.OrganizationId,
            targetStatus,
            actorId);

        return Result<JobReportResponse>.Success(report);
    }

    public async Task<Result<JobLinkResponse>> CreateLinkAsync(Guid reportId, CreateJobLinkRequest request, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobLinkResponse>.Unauthorized();
        }

        if (reportId == request.TargetReportId)
        {
            return Result<JobLinkResponse>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "TargetReportId", ErrorMessage = "En sag kan ikke linkes til sig selv." }
            });
        }

        var report = await repository.GetAsync(reportId, organizationId.Value, cancellationToken);
        if (report is null)
        {
            return Result<JobLinkResponse>.NotFound();
        }

        var target = await repository.GetAsync(request.TargetReportId, organizationId.Value, cancellationToken);
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

        var link = await linkRepository.CreateLinkAsync(organizationId.Value, reportId, request.TargetReportId, request.LinkType, cancellationToken);
        await InvalidateJobCachesAsync(reportId, organizationId.Value, cancellationToken);
        await InvalidateJobCachesAsync(request.TargetReportId, organizationId.Value, cancellationToken);
        logger.LogInformation("Job link created. SourceReportId: {SourceReportId}. TargetReportId: {TargetReportId}. LinkType: {LinkType}.",
            reportId, request.TargetReportId, request.LinkType);

        return Result<JobLinkResponse>.Success(link);
    }

    public async Task<Result<IReadOnlyList<JobLinkResponse>>> GetLinksAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<IReadOnlyList<JobLinkResponse>>.Unauthorized();
        }

        var report = await repository.GetAsync(reportId, organizationId.Value, cancellationToken);
        if (report is null)
        {
            return Result<IReadOnlyList<JobLinkResponse>>.NotFound();
        }

        var links = await linkRepository.GetLinksAsync(organizationId.Value, reportId, cancellationToken);
        return Result<IReadOnlyList<JobLinkResponse>>.Success(links);
    }

    public async Task<Result> DeleteLinkAsync(Guid reportId, Guid linkId, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result.Unauthorized();
        }

        var report = await repository.GetAsync(reportId, organizationId.Value, cancellationToken);
        if (report is null)
        {
            return Result.NotFound();
        }

        var link = await linkRepository.GetLinkAsync(organizationId.Value, linkId, cancellationToken);
        if (link is null)
        {
            return Result.NotFound();
        }

        var deleted = await linkRepository.DeleteLinkAsync(organizationId.Value, linkId, cancellationToken);
        if (!deleted)
        {
            return Result.NotFound();
        }

        await InvalidateJobCachesAsync(link.ReportId, organizationId.Value, cancellationToken);
        await InvalidateJobCachesAsync(link.LinkedReportId, organizationId.Value, cancellationToken);
        logger.LogInformation("Job link deleted. LinkId: {LinkId}. ReportId: {ReportId}.", linkId, reportId);
        return Result.Success();
    }

     public async Task<Result<JobReportResponse>> DeleteAsync(Guid id, CancellationToken cancellationToken)
     {
         var organizationId = currentUser.OrganizationId;
         if (organizationId is null)
         {
             return Result<JobReportResponse>.Unauthorized();
         }

         var deleted = await repository.DeleteAsync(id, organizationId.Value, cancellationToken);
         if (deleted is null)
         {
             logger.LogWarning("Job delete returned not found. JobId: {JobId}.", id);
             return Result<JobReportResponse>.NotFound();
         }

         await InvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
         logger.LogInformation("Job soft deleted. JobId: {JobId}.", id);

         return Result<JobReportResponse>.Success(deleted);
     }

     public async Task<Result<JobReportResponse>> RestoreDeletionAsync(Guid id, CancellationToken cancellationToken)
     {
         var organizationId = currentUser.OrganizationId;
         if (organizationId is null)
         {
             return Result<JobReportResponse>.Unauthorized();
         }

         var restored = await repository.RestoreDeletionAsync(id, organizationId.Value, cancellationToken);
         if (restored is null)
         {
             logger.LogWarning("Job restore deletion returned not found. JobId: {JobId}.", id);
             return Result<JobReportResponse>.NotFound();
         }

         await InvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
         logger.LogInformation("Job deletion restored. JobId: {JobId}.", id);

         return Result<JobReportResponse>.Success(restored);
     }

     public async Task<Result<JobReportResponse>> AssignAsync(Guid jobId, Guid? userId, CancellationToken cancellationToken)
     {
         var organizationId = currentUser.OrganizationId;
         if (organizationId is null)
         {
             return Result<JobReportResponse>.Unauthorized();
         }

         var assigned = await repository.AssignAsync(jobId, organizationId.Value, userId, currentUser.UserId, cancellationToken);
         if (assigned is null)
         {
             return Result<JobReportResponse>.NotFound(); 
         }

         await InvalidateJobCachesAsync(jobId, organizationId.Value, cancellationToken);
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
            report.SoftDeleted,
            report.DeletionScheduledAt);
    }

    private async Task<List<ValidationError>> ValidateDraftTaxonomyAsync(
        string? workKind,
        string? customWorkKind,
        IReadOnlyList<string>? closureFlags,
        CancellationToken cancellationToken)
    {
        var taxonomy = await taxonomyRepository.GetAsync(cancellationToken);
        return ValidateDraftTaxonomy(workKind, customWorkKind, closureFlags, taxonomy);
    }

    private static List<ValidationError> ValidateDraftTaxonomy(
        string? workKind,
        string? customWorkKind,
        IReadOnlyList<string>? closureFlags,
        JobTaxonomySnapshot taxonomy)
    {
        var errors = new List<ValidationError>();
        var normalizedWorkKind = string.IsNullOrWhiteSpace(workKind) ? null : workKind.Trim();

        if (normalizedWorkKind is null)
        {
            if (!string.IsNullOrWhiteSpace(customWorkKind))
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.CustomWorkKind), ErrorMessage = "Custom work kind requires a work kind." });
            }
        }
        else if (!taxonomy.WorkKinds.TryGetValue(normalizedWorkKind, out var workKindDefinition))
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = $"Unknown work kind '{normalizedWorkKind}'." });
        }
        else if (!workKindDefinition.RequiresCustomWorkKind && !string.IsNullOrWhiteSpace(customWorkKind))
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.CustomWorkKind), ErrorMessage = "Custom work kind is only allowed for work kinds that require custom text." });
        }

        if (closureFlags is not null)
        {
            var normalizedClosureFlags = closureFlags
                .Where(flag => !string.IsNullOrWhiteSpace(flag))
                .Select(flag => flag.Trim())
                .ToArray();

            foreach (var flagId in normalizedClosureFlags.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!taxonomy.ClosureFlags.ContainsKey(flagId))
                {
                    errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.ClosureFlags), ErrorMessage = $"Unknown closure flag '{flagId}'." });
                }
            }

            var hasExclusiveFlag = normalizedClosureFlags.Any(flagId =>
                taxonomy.ClosureFlags.TryGetValue(flagId, out var flag) && flag.IsExclusive);
            if (hasExclusiveFlag && normalizedClosureFlags.Length > 1)
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.ClosureFlags), ErrorMessage = "Exclusive closure flags cannot be combined with other closure flags." });
            }
        }

        return errors;
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

    private async Task InvalidateJobCachesAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        await cache.RemoveByTagAsync(JobListTag(organizationId), cancellationToken);
        await cache.RemoveByTagAsync(JobReportTag(id, organizationId), cancellationToken);
    }

    private static string JobReportCacheKey(Guid id, Guid organizationId) => $"jobs:detail:{organizationId:N}:{id:N}";

    private static string JobReportTag(Guid id, Guid organizationId) => $"jobs:detail:{organizationId:N}:{id:N}";

    private static string JobListTag(Guid organizationId) => $"jobs:list:{organizationId:N}";

    private static string ValidationFields(IEnumerable<ValidationError> errors) =>
        string.Join(",", errors.Select(error => error.Identifier).Distinct());

    private sealed record CachedJobReport(bool Found, JobReportResponse? Value)
    {
        public static CachedJobReport From(JobReportResponse? value) => new(value is not null, value);
    }
}
