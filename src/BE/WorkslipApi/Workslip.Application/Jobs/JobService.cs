using Ardalis.Result;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Application.Jobs;

public sealed class JobService(
    IJobRepository _jobRepository,
    IAssignmentRepository assignmentRepository,
    IJobLinkRepository linkRepository,
    IReferenceDataRepository referenceDataRepository,
    IUserRepository userRepository,
    HybridCache cache,
    IValidator<CreateJobRequest> createJobValidator,
    IValidator<UpdateJobRequest> updateJobValidator,
    IValidator<ChangeJobStatusRequest> changeJobStatusValidator,
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

    public async Task<Result<JobReportSummaryResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;

        if (organizationId is null)
        {
            return Result<JobReportSummaryResponse>.Unauthorized();
        }

        var validationResult = await createJobValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = MapValidationErrors(validationResult);
            logger.LogWarning("Job create validation failed. OrganizationId: {OrganizationId}. Fields: {ValidationFields}",
                organizationId.Value,
                ValidationFields(errors));

            return Result<JobReportSummaryResponse>.Invalid(errors);
        }

        var workErrors = await ValidateDraftWorkAsync(organizationId.Value, request.Work, cancellationToken);
        if (workErrors.Count != 0)
        {
            logger.LogWarning("Job create taxonomy validation failed. OrganizationId: {OrganizationId}. Fields: {ValidationFields}",
                organizationId.Value,
                ValidationFields(workErrors));

            return Result<JobReportSummaryResponse>.Invalid(workErrors);
        }

        var installationSelectionErrors = await ValidateInstallationSelectionsAsync(
            organizationId.Value,
            request.Work?.InstallationTypes,
            cancellationToken);
        if (installationSelectionErrors.Count != 0)
        {
            logger.LogWarning("Job create installation selection validation failed. OrganizationId: {OrganizationId}. Fields: {ValidationFields}",
                organizationId.Value,
                ValidationFields(installationSelectionErrors));

            return Result<JobReportSummaryResponse>.Invalid(installationSelectionErrors);
        }

        var actorId = currentUser.UserId;
        var assignedUserIds = actorId.HasValue ? [actorId.Value] : Array.Empty<Guid>();
        var created = await _jobRepository.CreateAsync(organizationId.Value, request, assignedUserIds, actorId, cancellationToken);
        await InvalidateJobCachesAsync(created.Id, created.OrganizationId, cancellationToken);
        LogJobCreated(created);

        return await ToSummaryResultAsync(created, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<JobListItemResponse>>> ListAsync(
        JobStatus? status,
        string? reportNumber,
        string? customerName,
        string? customerEmail,
        string? customerAddress,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<IReadOnlyList<JobListItemResponse>>.Unauthorized();
        }

        var searchErrors = ValidateSearchFilters(reportNumber, customerName, customerEmail, customerAddress);
        if (searchErrors.Count > 0)
        {
            return Result<IReadOnlyList<JobListItemResponse>>.Invalid(searchErrors);
        }

        var query = BuildJobQuery(organizationId.Value, status, reportNumber, customerName, customerEmail, customerAddress, limit, offset);

        var cacheKey = BuildJobListCacheKey(query);
        var jobs = await cache.GetOrCreateAsync(
            cacheKey,
            async token => (await _jobRepository.ListAsync(query, token)).ToArray(),
            JobListCacheOptions,
            tags: ["jobs", JobListTag(query.OrganizationId)],
            cancellationToken: cancellationToken);

        return Result<IReadOnlyList<JobListItemResponse>>.Success(jobs);
    }

    public async Task<Result<JobReportSummaryResponse>> GetSingleJobAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await GetJobAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Status == ResultStatus.Unauthorized
                ? Result<JobReportSummaryResponse>.Unauthorized()
                : Result<JobReportSummaryResponse>.NotFound();
        }
        var organizationId = currentUser.OrganizationId;
        
        if (organizationId == Guid.Empty)
        {
            return Result<JobReportSummaryResponse>.Unauthorized();
        }

        var referenceData = await referenceDataRepository.GetAsync(organizationId, cancellationToken);
        var summary = ToSummary(result.Value, referenceData);

        logger.LogInformation("Job report summary fetched. JobId: {JobId}. OrganizationId: {OrganizationId}. Status: {Status}.",
            summary.Id,
            summary.OrganizationId,
            summary.Status);

        return Result<JobReportSummaryResponse>.Success(summary);
    }

    private async Task<Result<JobReportResponse>> GetJobAsync(Guid id, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportResponse>.Unauthorized();
        }

        var orgId = organizationId.Value;

        var cached = await cache.GetOrCreateAsync(JobReportCacheKey(id, orgId),
            async token => CachedJobReport.From(await _jobRepository.GetSingleJobAsync(id, orgId, token)),
            JobReportCacheOptions, tags: ["jobs", JobReportTag(id, orgId)],
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

    public async Task<Result<IReadOnlyList<JobEventResponse>>> GetHistoryAsync(Guid id, int? limit, int? offset, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<IReadOnlyList<JobEventResponse>>.Unauthorized();
        }

        var normalizedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var normalizedOffset = Math.Max(offset ?? 0, 0);
        var events = await _jobRepository.GetEventsAsync(id, organizationId.Value, normalizedLimit, normalizedOffset, cancellationToken);
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

    public async Task<Result<JobReportSummaryResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await updateJobValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = MapValidationErrors(validationResult);
            logger.LogWarning("Job update validation failed. JobId: {JobId}. Fields: {ValidationFields}",
                id,
                ValidationFields(errors));

            return Result<JobReportSummaryResponse>.Invalid(errors);
        }

        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportSummaryResponse>.Unauthorized();
        }

        if (request.Work is not null)
        {
            var workErrors = await ValidateDraftWorkAsync(organizationId.Value, request.Work, cancellationToken);
            if (workErrors.Count != 0)
            {
                logger.LogWarning("Job update work validation failed. JobId: {JobId}. Fields: {ValidationFields}",
                    id,
                    ValidationFields(workErrors));

                return Result<JobReportSummaryResponse>.Invalid(workErrors);
            }

            var installationSelectionErrors = await ValidateInstallationSelectionsAsync(
                organizationId.Value,
                request.Work.InstallationTypes,
                cancellationToken);
            if (installationSelectionErrors.Count != 0)
            {
                logger.LogWarning("Job update installation selection validation failed. JobId: {JobId}. Fields: {ValidationFields}",
                    id,
                    ValidationFields(installationSelectionErrors));

                return Result<JobReportSummaryResponse>.Invalid(installationSelectionErrors);
            }
        }

        var updated = await _jobRepository.UpdateAsync(id, organizationId.Value, request, cancellationToken);
        if (updated is null)
        {
            logger.LogWarning("Job update returned not found. JobId: {JobId}.", id);
            return Result<JobReportSummaryResponse>.NotFound();
        }

        await InvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
        LogJobUpdated(updated);

        return await ToSummaryResultAsync(updated, cancellationToken);
    }

    public async Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(Guid id, ChangeJobStatusRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await changeJobStatusValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Result<JobReportSummaryResponse>.Invalid(MapValidationErrors(validationResult));
        }

        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportSummaryResponse>.Unauthorized();
        }

        if (request.Status == JobStatus.Submitted)
        {
            var result = await ValidateSubmitReadyAsync(id, organizationId.Value, cancellationToken);
            if (result is not null) return result;
        }

        return await TransitionAsync(id, request.Status, cancellationToken);
    }

    private async Task<Result<JobReportSummaryResponse>> TransitionAsync(Guid id, JobStatus targetStatus, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportSummaryResponse>.Unauthorized();
        }

        var actorId = currentUser.UserId;
        var report = await _jobRepository.TransitionAsync(id, organizationId.Value, targetStatus, actorId, cancellationToken);
        if (report is null)
        {
            logger.LogWarning("Job transition returned not found. JobId: {JobId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
                id,
                targetStatus,
                actorId);

            return Result<JobReportSummaryResponse>.NotFound();
        }

        await InvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
        logger.LogInformation("Job transitioned. JobId: {JobId}. OrganizationId: {OrganizationId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
            report.Id,
            report.OrganizationId,
            targetStatus,
            actorId);

        return await ToSummaryResultAsync(report, cancellationToken);
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
            return Result<JobLinkResponse>.Invalid([new ValidationError { Identifier = "TargetReportId", ErrorMessage = "En sag kan ikke linkes til sig selv." }]);
        }

        var validationError = await ValidateLinkTargetAsync(reportId, request.TargetReportId, organizationId.Value, cancellationToken);
        if (validationError is not null)
        {
            return Result<JobLinkResponse>.Invalid([validationError]);
        }

        if (await HasExistingLinkAsync(reportId, request.TargetReportId, organizationId.Value, cancellationToken))
        {
            return Result<JobLinkResponse>.Invalid([new ValidationError { Identifier = "TargetReportId", ErrorMessage = "Man kan ikke assigne samme sag to gange" }]);
        }

        var link = await linkRepository.CreateLinkAsync(organizationId.Value, reportId, request.TargetReportId, request.LinkType, cancellationToken);
        await InvalidateJobCachesAsync(reportId, organizationId.Value, cancellationToken);
        await InvalidateJobCachesAsync(request.TargetReportId, organizationId.Value, cancellationToken);
        logger.LogInformation("Job link created. SourceReportId: {SourceReportId}. TargetReportId: {TargetReportId}. LinkType: {LinkType}.",
            reportId, request.TargetReportId, request.LinkType);

        return Result<JobLinkResponse>.Success(link);
    }

    public async Task<Result<IReadOnlyList<JobListItemResponse>>> GetMyAssignedJobsAsync(CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        var userId = currentUser.UserId;

        if (organizationId is null || userId is null)
        {
            return Result<IReadOnlyList<JobListItemResponse>>.Unauthorized();
        }

        var jobs = await assignmentRepository.GetMyAssignedJobsAsync(organizationId.Value, userId.Value, cancellationToken);
        
        return Result<IReadOnlyList<JobListItemResponse>>.Success(jobs);    
    }

    public async Task<Result> DeleteLinkAsync(Guid reportId, Guid linkId, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result.Unauthorized();
        }

        var report = await _jobRepository.GetSingleJobAsync(reportId, organizationId.Value, cancellationToken);
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

        await InvalidateJobCachesAsync(link.SourceReportId, organizationId.Value, cancellationToken);
        await InvalidateJobCachesAsync(link.TargetReportId, organizationId.Value, cancellationToken);
        logger.LogInformation("Job link deleted. LinkId: {LinkId}. ReportId: {ReportId} SourceId {SourceId} TargetReportId {TargetReportId}", linkId, reportId, link.SourceReportId, link.TargetReportId);
        return Result.Success();
    }

     public async Task<Result<JobReportSummaryResponse>> DeleteAsync(Guid id, CancellationToken cancellationToken)
     {
         var organizationId = currentUser.OrganizationId;
         if (organizationId is null)
         {
             return Result<JobReportSummaryResponse>.Unauthorized();
         }

         var deleted = await _jobRepository.DeleteAsync(id, organizationId.Value, cancellationToken);
         if (deleted is null)
         {
             logger.LogWarning("Job delete returned not found. JobId: {JobId}.", id);
             return Result<JobReportSummaryResponse>.NotFound();
         }

         await InvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
         logger.LogInformation("Job soft deleted. JobId: {JobId}.", id);

         return await ToSummaryResultAsync(deleted, cancellationToken);
     }

     public async Task<Result<JobReportSummaryResponse>> RestoreDeletionAsync(Guid id, CancellationToken cancellationToken)
     {
         var organizationId = currentUser.OrganizationId;
         if (organizationId is null)
         {
             return Result<JobReportSummaryResponse>.Unauthorized();
         }

         var restored = await _jobRepository.RestoreDeletionAsync(id, organizationId.Value, cancellationToken);
         if (restored is null)
         {
             logger.LogWarning("Job restore deletion returned not found. JobId: {JobId}.", id);
             return Result<JobReportSummaryResponse>.NotFound();
         }

         await InvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
         logger.LogInformation("Job deletion restored. JobId: {JobId}.", id);

         return await ToSummaryResultAsync(restored, cancellationToken);
     }

     public async Task<Result<JobReportSummaryResponse>> AssignAsync(Guid jobId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
     {
         var organizationId = currentUser.OrganizationId;
            
         if (organizationId is null)
         {
             return Result<JobReportSummaryResponse>.Unauthorized();
         }

        var invalidUserError = await ValidateAssignedUsersExistAsync(userIds, jobId, organizationId.Value, cancellationToken);
        if (invalidUserError is not null) 
            return invalidUserError;

         await assignmentRepository.AssignAsync(jobId, organizationId.Value, userIds, currentUser.UserId, cancellationToken);
        
        var job = await _jobRepository.GetSingleJobAsync(jobId, organizationId.Value, cancellationToken);
        if (job is null)
         {
             return Result<JobReportSummaryResponse>.NotFound();
         }

         await InvalidateJobCachesAsync(jobId, organizationId.Value, cancellationToken);
         logger.LogInformation("Job assigned. JobId: {JobId}. AssignedUserCount: {Assigneds}.", jobId, userIds);

         return await ToSummaryResultAsync(job, cancellationToken);
     }

    private async Task<Result<JobReportSummaryResponse>> ToSummaryResultAsync(JobReportResponse report, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        var referenceData = organizationId.HasValue
            ? await referenceDataRepository.GetAsync(organizationId.Value, cancellationToken)
            : null;
        return Result<JobReportSummaryResponse>.Success(ToSummary(report, referenceData));
    }

    private static JobReportSummaryResponse ToSummary(JobReportResponse report, ReferenceDataResponse referenceData)
    {

        var closureFlags = report.ClosureFlags
            .Select(cf => {
                
                var flagDefinition = referenceData.ClosureFlags.FirstOrDefault(x => x.Id == cf.Id);
                if (flagDefinition == null)
                    return null;

                var closureFlag = new JobReportSummaryClosureFlagResponse(
                    flagDefinition.Id,
                    flagDefinition.NormalizedLabel,
                    flagDefinition.Label);

                return closureFlag;
                }).Where(x => x != null).ToList();

        return new(
            report.Id,
            report.OrganizationId,
            report.ReportNumber,
            report.Status,
            report.Customer ?? new CustomerInfo(null, null, null, null, null, null),
            new JobReportSummaryWorkResponse(
                report.WorkKind,
                report.InstallationTypes,
                closureFlags!,
                report.Remarks),
            new JobReportSummaryObservationResponse(
                report.ReportDate,
                report.TaskDescription,
                report.CustomerObservations,
                report.TechnicalObservations),
            Array.Empty<ControlInstallationTypeResponse>(),
            report.Links,
            report.CreatedAt,
            report.UpdatedAt,
            report.SubmittedAt,
            report.AssignedUsers,
            report.SoftDeleted,
            report.DeletionScheduledAt);
    }

    private async Task<List<ValidationError>> ValidateDraftWorkAsync(Guid organizationId, CreateJobWorkRequest? workind, CancellationToken cancellationToken)
    {
        var refData = await referenceDataRepository.GetAsync(organizationId, cancellationToken);
        return ValidateDraftWork(workind, refData);
    }

    private async Task<List<ValidationError>> ValidateInstallationSelectionsAsync(
        Guid organizationId,
        IReadOnlyList<CreateInstallationTypeRequest>? installationTypes,
        CancellationToken cancellationToken)
    {
        if (installationTypes is null || installationTypes.Count == 0)
        {
            return [];
        }

        var referenceData = await referenceDataRepository.GetAsync(organizationId, cancellationToken);
        return JobInstallationSelectionValidator.Validate(installationTypes, referenceData);
    }

    private static List<ValidationError> ValidateDraftWork(CreateJobWorkRequest? workKind, ReferenceDataResponse referenceData)
    {
        var errors = new List<ValidationError>();

        if (workKind is null)
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = "Work kind is required." });
            return errors;
        }

        var normalizedWorkKind = string.IsNullOrWhiteSpace(workKind?.WorkKind) ? null : workKind.WorkKind.Trim();

        var workKindsByLabel = referenceData.WorkKinds.ToDictionary(w => w.NormalizedLabel, StringComparer.OrdinalIgnoreCase);
        var closureFlagsByLabel = referenceData.ClosureFlags.ToDictionary(f => f.NormalizedLabel, StringComparer.OrdinalIgnoreCase);

        if (normalizedWorkKind is null)
        {
            if (!string.IsNullOrWhiteSpace(workKind.CustomWorkKind))
            {
                errors.Add(new ValidationError { Identifier = $"{nameof(JobReportResponse.WorkKind)}.{nameof(JobWorkKindResponse.CustomWorkKind)}", ErrorMessage = "Custom work kind requires a work kind." });
            }
        }
        else if (!workKindsByLabel.TryGetValue(normalizedWorkKind, out var workKindDefinition))
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = $"Unknown work kind '{normalizedWorkKind}'." });
        }
        else if (!workKindDefinition.RequiresCustomWorkKind && !string.IsNullOrWhiteSpace(workKind.CustomWorkKind))
        {
            errors.Add(new ValidationError { Identifier = $"{nameof(JobReportResponse.WorkKind)}.{nameof(JobWorkKindResponse.CustomWorkKind)}", ErrorMessage = "Custom work kind is only allowed for work kinds that require custom text." });
        }

        if (workKind.ClosureFlags is not null)
        {
            var normalizedClosureFlags = workKind.ClosureFlags
                .Where(flag => !string.IsNullOrWhiteSpace(flag))
                .Select(flag => flag.Trim())
                .ToArray();

            foreach (var flagId in normalizedClosureFlags.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!closureFlagsByLabel.ContainsKey(flagId))
                {
                    errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.ClosureFlags), ErrorMessage = $"Unknown closure flag '{flagId}'." });
                }
            }

            var hasExclusiveFlag = normalizedClosureFlags.Any(flagId =>
                closureFlagsByLabel.TryGetValue(flagId, out var flag) && flag.IsExclusive);
            if (hasExclusiveFlag && normalizedClosureFlags.Length > 1)
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.ClosureFlags), ErrorMessage = "Exclusive closure flags cannot be combined with other closure flags." });
            }
        }

        return errors;
    }

    private static List<ValidationError> ValidateReadyForSubmission(JobReportResponse report, ReferenceDataResponse referenceData)
    {
        var errors = new List<ValidationError>();
        AddRequired(errors, nameof(JobReportResponse.ReportNumber), report.ReportNumber, "Report number is required.");
        AddRequired(errors, $"{nameof(JobReportResponse.Customer)}.{nameof(CustomerInfo.Name)}", report.Customer?.Name, "Customer name is required.");
        AddRequired(errors, $"{nameof(JobReportResponse.Customer)}.{nameof(CustomerInfo.Address)}", report.Customer?.Address, "Customer address is required.");
        AddRequired(errors, nameof(JobReportResponse.TaskDescription), report.TaskDescription, "Task description is required.");

        if (report.InstallationTypes.Count == 0)
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.InstallationTypes), ErrorMessage = "Select at least one installation type." });
        }

        var workKindsByLabel = referenceData.WorkKinds
            .ToDictionary(w => w.NormalizedLabel, StringComparer.OrdinalIgnoreCase);

        if (report.WorkKind is null)
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = "Work kind is required." });
        }
        else if (!workKindsByLabel.TryGetValue(report.WorkKind.NormalizedLabel, out var workKind))
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = $"Unknown work kind '{report.WorkKind.NormalizedLabel}'." });
        }
        else if (workKind.RequiresCustomWorkKind && string.IsNullOrWhiteSpace(report.WorkKind.CustomWorkKind))
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = "Custom work kind is required for this work kind." });
        }
        else if (!workKind.RequiresCustomWorkKind && !string.IsNullOrWhiteSpace(report.WorkKind.CustomWorkKind))
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = "Custom work kind is only allowed for work kinds that require custom text." });
        }

        return errors;
    }

    private async Task<Result<JobReportSummaryResponse>?> ValidateSubmitReadyAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        var current = await _jobRepository.GetSingleJobAsync(id, organizationId, cancellationToken);
        if (current is null)
        {
            logger.LogWarning("Job submit returned not found. JobId: {JobId}.", id);
            return Result<JobReportSummaryResponse>.NotFound();
        }

        var refData = await referenceDataRepository.GetAsync(organizationId, cancellationToken);
        var validationErrors = ValidateReadyForSubmission(current, refData);
        if (validationErrors.Count == 0) return null;

        logger.LogWarning("Job submit validation failed. JobId: {JobId}. Fields: {ValidationFields}",
            id, ValidationFields(validationErrors));

        return Result<JobReportSummaryResponse>.Invalid(validationErrors);
    }

    private static List<ValidationError> ValidateSearchFilters(
        string? reportNumber, string? customerName, string? customerEmail, string? customerAddress)
    {
        var errors = new List<ValidationError>();
        if (reportNumber?.Length > 0 && reportNumber.Length < 2)
            errors.Add(new() { Identifier = nameof(reportNumber), ErrorMessage = "Søgning på rapportnummer skal være på mindst 2 tegn." });
        if (customerName?.Length > 0 && customerName.Length < 2)
            errors.Add(new() { Identifier = nameof(customerName), ErrorMessage = "Søgning på navn skal være på mindst 2 tegn." });
        if (customerEmail?.Length > 0 && customerEmail.Length < 2)
            errors.Add(new() { Identifier = nameof(customerEmail), ErrorMessage = "Søgning på e-mail skal være på mindst 2 tegn." });
        if (customerAddress?.Length > 0 && customerAddress.Length < 2)
            errors.Add(new() { Identifier = nameof(customerAddress), ErrorMessage = "Søgning på adresse skal være på mindst 2 tegn." });
        return errors;
    }

    private static JobQuery BuildJobQuery(
        Guid organizationId, JobStatus? status,
        string? reportNumber, string? customerName, string? customerEmail, string? customerAddress,
        int? limit, int? offset)
    {
        var normalizedReportSearch = string.IsNullOrWhiteSpace(reportNumber) ? null : reportNumber.Trim();
        var normalizedNameSearch = string.IsNullOrWhiteSpace(customerName) ? null : customerName.Trim();
        var normalizedEmailSearch = string.IsNullOrWhiteSpace(customerEmail) ? null : customerEmail.Trim();
        var normalizedAddressSearch = string.IsNullOrWhiteSpace(customerAddress) ? null : customerAddress.Trim();

        return new JobQuery(organizationId, status, Math.Clamp(limit ?? 50, 1, 200), Math.Max(offset ?? 0, 0),
            normalizedReportSearch, normalizedNameSearch, normalizedEmailSearch, normalizedAddressSearch);
    }

    private static string BuildJobListCacheKey(JobQuery query) =>
        $"jobs:list:organization={query.OrganizationId:N}:status={query.Status?.ToString() ?? "all"}" +
        $":reportNumber={query.ReportNumber ?? "none"}:customerName={query.CustomerName ?? "none"}" +
        $":customerEmail={query.CustomerEmail ?? "none"}:customerAddress={query.CustomerAddress ?? "none"}:limit={query.Limit}:offset={query.Offset}";

    private async Task<ValidationError?> ValidateLinkTargetAsync(Guid reportId, Guid targetId, Guid organizationId, CancellationToken cancellationToken)
    {
        var report = await _jobRepository.GetSingleJobAsync(reportId, organizationId, cancellationToken);
        if (report is null) return null;

        var target = await _jobRepository.GetSingleJobAsync(targetId, organizationId, cancellationToken);
        if (target is null)
        {
            return new ValidationError { Identifier = "TargetReportId", ErrorMessage = "Den valgte sag findes ikke." };
        }

        if (report.OrganizationId != target.OrganizationId)
        {
            return new ValidationError { Identifier = "TargetReportId", ErrorMessage = "Kunne ikke finde den sag du referer til." };
        }

        return null;
    }

    private async Task<bool> HasExistingLinkAsync(Guid reportId, Guid targetId, Guid organizationId, CancellationToken cancellationToken)
    {
        var report = await _jobRepository.GetSingleJobAsync(reportId, organizationId, cancellationToken);
        return report?.Links.Any(x => x.LinkedReportId == targetId) ?? false;
    }

    private async Task<Result<JobReportSummaryResponse>?> ValidateAssignedUsersExistAsync(
        IReadOnlyList<Guid>? userIds, Guid jobId, Guid organizationId, CancellationToken cancellationToken)
    {
        if (userIds is null || userIds.Count == 0) 
            return null;

        foreach (var userId in userIds)
        {
            var assignedUser = await userRepository.GetByIdAsync(userId, cancellationToken);
            if (assignedUser is not null) 
                continue;

            logger.LogWarning("Job assignment validation failed. JobId: {JobId}. OrganizationId: {OrganizationId}. InvalidAssignedUserId: {InvalidAssignedUserId}.",
                jobId, organizationId, userId);

            return Result<JobReportSummaryResponse>.Invalid([new ValidationError
            {
                Identifier = nameof(AssignJobRequest.UserIds),
                ErrorMessage = "En eller flere valgte brugere findes ikke i organisationen."
            }]);
        }

        return null;
    }

    private static List<ValidationError> MapValidationErrors(ValidationResult result) =>
        result.Errors
            .Select(e => new ValidationError { Identifier = e.PropertyName, ErrorMessage = e.ErrorMessage })
            .ToList();

    private void LogJobCreated(JobReportResponse job) =>
        logger.LogInformation(
            "Job created. JobId: {JobId}. OrganizationId: {OrganizationId}. Status: {Status}. ReportNumber: {ReportNumber}. WorkKindId: {WorkKindId}. AssignedUserCount: {AssignedUserCount}. InstallationTypeCount: {InstallationTypeCount}.",
            job.Id, job.OrganizationId, job.Status, job.ReportNumber, job.WorkKind?.Id, job.AssignedUsers.Count, job.InstallationTypes.Count);

    private void LogJobUpdated(JobReportResponse job) =>
        logger.LogInformation(
            "Job updated. JobId: {JobId}. OrganizationId: {OrganizationId}. Status: {Status}. ReportNumber: {ReportNumber}. WorkKindId: {WorkKindId}. InstallationTypeCount: {InstallationTypeCount}.",
            job.Id, job.OrganizationId, job.Status, job.ReportNumber, job.WorkKind?.Id, job.InstallationTypes.Count);

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
