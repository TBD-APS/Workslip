using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Ardalis.Result;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Application.Worksheets;
using Workslip.Application.Notifications;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

public sealed class JobService(
    IJobRepository _jobRepository,
    IJobViewRepository _jobViewRepository,
    IAssignmentRepository assignmentRepository,
    IJobLinkRepository linkRepository,
    IReferenceDataRepository referenceDataRepository,
    IWorksheetRepository worksheetRepository,
    HybridCache cache,
    IValidator<CreateJobRequest> createJobValidator,
    IValidator<UpdateJobRequest> updateJobValidator,
    IValidator<ChangeJobStatusRequest> changeJobStatusValidator,
    ICurrentUserContext currentUser,
    ILogger<JobService> logger,
    JobValidationService jobValidationService,
    INotificationService notificationService,
    JobDeletionNotificationService jobDeletionNotificationService,
    JobLifecycleService? jobLifecycleService = null) : IJobService
{
    private readonly JobLifecycleService _jobLifecycleService = jobLifecycleService ?? new JobLifecycleService(
        _jobRepository,
        _jobViewRepository,
        assignmentRepository,
        referenceDataRepository,
        worksheetRepository,
        cache,
        changeJobStatusValidator,
        currentUser,
        logger,
        jobValidationService,
        notificationService);

    private static readonly HybridCacheEntryOptions JobReportCacheOptions = new()
    {
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    private static readonly HybridCacheEntryOptions JobListCacheOptions = new()
    {
        LocalCacheExpiration = TimeSpan.FromSeconds(15)
    };

    /// <summary>
    /// The sort columns the repository names in its ordering switch. Used only to keep the
    /// readable part of a job list cache key legible - see <see cref="SortKeyPart"/>.
    /// </summary>
    private static readonly string[] JobListSortColumns =
        ["name", "address", "reportNumber", "createdAt", "updatedAt", "reportDate"];

    /// <summary>
    /// 32 hex characters, so 128 bits of the SHA-256 digest. A cache key is not a security
    /// boundary, but a collision here merges two result sets, so the margin is deliberate:
    /// at a million distinct job list keys the birthday probability is about 1.5e-27, and a
    /// chosen collision still costs a 2^64 search because the digest is cryptographic.
    /// </summary>
    private const int JobListFingerprintHexChars = 32;

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

        // Resolve JobType (default to KLS)
        var jobType = string.IsNullOrWhiteSpace(request.JobType) ? JobType.KLS : Enum.Parse<JobType>(request.JobType);

        // Skip work/installation validation for Diverse jobs
        if (jobType != JobType.Diverse && request.Work is not null)
        {
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
                request.Work.InstallationTypes,
                cancellationToken);
            if (installationSelectionErrors.Count != 0)
            {
                logger.LogWarning("Job create installation selection validation failed. OrganizationId: {OrganizationId}. Fields: {ValidationFields}",
                    organizationId.Value,
                    ValidationFields(installationSelectionErrors));

                return Result<JobReportSummaryResponse>.Invalid(installationSelectionErrors);
            }
        }

        var linkedJobIds = request.LinkedJobIds ?? [];
        foreach (var linkedJobId in linkedJobIds)
        {
            if (await _jobRepository.GetSingleJobAsync(linkedJobId, organizationId.Value, cancellationToken) is null)
            {
                return Result<JobReportSummaryResponse>.Invalid([new ValidationError
                {
                    Identifier = nameof(CreateJobRequest.LinkedJobIds),
                    ErrorMessage = "Den valgte sammenkædede sag findes ikke."
                }]);
            }
        }

        var actorId = currentUser.UserId;
        var assignedUserIds = JobAssignmentPolicy.ResolveInitialAssignments(
            request.AssignedUserIds,
            actorId,
            currentUser.Role);
        try
        {
            var created = await _jobRepository.CreateAsync(organizationId.Value, request, assignedUserIds, actorId, cancellationToken);
            var createdJobIds = created.CreatedJobIds ?? [created.Id];
            foreach (var affectedJobId in createdJobIds.Concat(linkedJobIds).Distinct())
            {
                await InvalidateJobCachesAsync(affectedJobId, created.OrganizationId, cancellationToken);
            }

            // Notify assignees for every create path (normal single-job creation as
            // well as duplicate-per-assignee), not only the duplicate case. A job
            // created with assignees inline must notify them just like the explicit
            // assign endpoint does.
            await QueueJobAssignmentNotificationsAsync(created, createdJobIds, actorId, cancellationToken);

            LogJobCreated(created);

            return await ToSummaryResultAsync(created, cancellationToken);
        }
        catch (WorksheetDailyHoursExceededException)
        {
            logger.LogWarning("Job create rejected because daily worksheet hours would exceed the limit. OrganizationId: {OrganizationId}.", organizationId.Value);
            return Result<JobReportSummaryResponse>.Invalid([new ValidationError
            {
                Identifier = nameof(CreateJobRequest.Timesheets),
                ErrorMessage = WorksheetHourRules.DailyLimitMessage
            }]);
        }
        catch (DuplicateReportNumberException ex)
        {
            logger.LogWarning(ex, "Job create duplicate report number. OrganizationId: {OrganizationId}. ReportNumber: {ReportNumber}", organizationId.Value, ex.ReportNumber);
            return Result<JobReportSummaryResponse>.Conflict("duplicate_report_number");
        }

    }

    public async Task<Result<JobListResponse>> ListAsync(
        List<JobStatus>? statuses,
        string? reportNumber,
        string? customerName,
        string? customerEmail,
        string? customerAddress,
        string? search,
        string? sortBy,
        string? sortDirection,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobListResponse>.Unauthorized();
        }

        var searchErrors = ValidateSearchFilters(reportNumber, customerName, customerEmail, customerAddress, search);
        if (searchErrors.Count > 0)
        {
            return Result<JobListResponse>.Invalid(searchErrors);
        }

        var requiresAssignedJobScope = JobAssignmentPolicy.RequiresAssignedJobScope(currentUser.Role);
        var assignedToUserId = requiresAssignedJobScope
            ? currentUser.UserId
            : null;
        if (requiresAssignedJobScope && assignedToUserId is null)
        {
            return Result<JobListResponse>.Unauthorized();
        }

        var query = BuildJobQuery(
            organizationId.Value,
            statuses,
            reportNumber,
            customerName,
            customerEmail,
            customerAddress,
            search,
            sortBy,
            sortDirection,
            limit,
            offset,
            currentUser.UserId,
            assignedToUserId);

        var cacheKey = BuildJobListCacheKey(query);
        var result = await cache.GetOrCreateAsync(
            cacheKey,
            async token => await _jobRepository.ListAsync(query, token),
            JobListCacheOptions,
            tags: ["all", "jobs", JobListTag(query.OrganizationId)],
            cancellationToken: cancellationToken);

        return Result<JobListResponse>.Success(result);
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
        var worksheets = await worksheetRepository.ListByJobAsync(id, cancellationToken);
        var summary = ToSummary(result.Value, referenceData, worksheets, currentUser);

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
            JobReportCacheOptions, tags: ["all", "jobs", JobReportTag(id, orgId)],
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

    public async Task<Result<IReadOnlyList<JobHistoryResponse>>> GetHistoryAsync(Guid id, int? limit, int? offset, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<IReadOnlyList<JobHistoryResponse>>.Unauthorized();
        }

        var normalizedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var normalizedOffset = Math.Max(offset ?? 0, 0);
        var events = await _jobRepository.GetEventsAsync(id, organizationId.Value, normalizedLimit, normalizedOffset, cancellationToken);
        if (events is null)
        {
            logger.LogWarning("Job history lookup returned not found. JobId: {JobId}.", id);
            return Result<IReadOnlyList<JobHistoryResponse>>.NotFound();
        }

        logger.LogInformation("Job history fetched. JobId: {JobId}. Limit: {Limit}. Offset: {Offset}. EventCount: {EventCount}.",
            id,
            normalizedLimit,
            normalizedOffset,
            events.Count);

        return Result<IReadOnlyList<JobHistoryResponse>>.Success(events);
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

        // Resolve JobType from request (default to KLS)
        var jobType = Enum.TryParse<JobType>(request.JobType, out var parsed) ? parsed : JobType.KLS;

        // Skip work/installation validation for Diverse jobs
        if (jobType != JobType.Diverse && request.Work is not null)
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

        JobReportResponse? updated;
        try
        {
            updated = await _jobRepository.UpdateAsync(id, organizationId.Value, request, cancellationToken);
        }
        catch (WorksheetDailyHoursExceededException)
        {
            logger.LogWarning("Job update rejected because daily worksheet hours would exceed the limit. JobId: {JobId}. OrganizationId: {OrganizationId}.", id, organizationId.Value);
            return Result<JobReportSummaryResponse>.Invalid([new ValidationError
            {
                Identifier = nameof(UpdateJobRequest.Timesheets),
                ErrorMessage = WorksheetHourRules.DailyLimitMessage
            }]);
        }
        catch (DuplicateReportNumberException ex)
        {
            logger.LogWarning(ex, "Job update duplicate report number. JobId: {JobId}. OrganizationId: {OrganizationId}. ReportNumber: {ReportNumber}", id, organizationId.Value, ex.ReportNumber);
            return Result<JobReportSummaryResponse>.Conflict("duplicate_report_number");
        }

        if (updated is null)
        {
            logger.LogWarning("Job update returned not found. JobId: {JobId}.", id);
            return Result<JobReportSummaryResponse>.NotFound();
        }

        await InvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
        LogJobUpdated(updated);

        return await ToSummaryResultAsync(updated, cancellationToken);
    }

    public Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(
        Guid id,
        ChangeJobStatusRequest request,
        CancellationToken cancellationToken) =>
        _jobLifecycleService.ChangeStatusAsync(id, request, cancellationToken);

    public async Task<Result<JobReportSummaryResponse>> CreateLinksAsync(Guid reportId, CreateJobLinkRequest request, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportSummaryResponse>.Unauthorized();
        }

        foreach (var targetId in request.TargetReportIds)
        {
            if (reportId == targetId)
            {
                return Result<JobReportSummaryResponse>.Invalid([new ValidationError { Identifier = "TargetReportIds", ErrorMessage = "En sag kan ikke linkes til sig selv." }]);
            }

            var validationError = await ValidateLinkTargetAsync(reportId, targetId, organizationId.Value, cancellationToken);
            if (validationError is not null)
            {
                return Result<JobReportSummaryResponse>.Invalid([validationError]);
            }
        }

        await linkRepository.CreateLinksAsync(organizationId.Value, reportId, request.TargetReportIds, cancellationToken);

        foreach (var targetId in request.TargetReportIds.Distinct())
            await InvalidateJobCachesAsync(targetId, organizationId.Value, cancellationToken);

        await InvalidateJobCachesAsync(reportId, organizationId.Value, cancellationToken);

        var jobReport = await _jobRepository.GetSingleJobAsync(reportId, organizationId.Value, cancellationToken);

        if (jobReport is null)
        {
            logger.LogWarning("Job report lookup after link creation returned not found. JobId: {JobId}.", reportId);
            return Result<JobReportSummaryResponse>.NotFound();
        }

        logger.LogInformation("Job links created. SourceReportId: {SourceReportId}. TargetCount: {TargetCount}.",
            reportId, request.TargetReportIds.Count);

        return Result<JobReportSummaryResponse>.Success(await ToSummaryResultAsync(jobReport, cancellationToken));
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

    public async Task<Result> DeleteLinksAsync(Guid reportId, DeleteJobLinksRequest request, CancellationToken cancellationToken)
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

        if (request.LinkIds.Count == 0)
        {
            return Result.Success();
        }

        var links = await linkRepository.GetLinkRowsAsync(organizationId.Value, reportId, cancellationToken);
        var linksToDelete = links.Where(l => request.LinkIds.Contains(l.Id)).ToArray();

        if (linksToDelete.Length == 0)
        {
            return Result.Success();
        }

        var affectedIds = linksToDelete
            .SelectMany(l => new[] { l.SourceReportId, l.TargetReportId })
            .Distinct()
            .ToArray();

        await linkRepository.DeleteLinksAsync(organizationId.Value, linksToDelete.Select(l => l.Id).ToArray(), cancellationToken);

        foreach (var affectedId in affectedIds)
        {
            await InvalidateJobCachesAsync(affectedId, organizationId.Value, cancellationToken);
        }

        logger.LogInformation("Job links deleted. LinkIds: {LinkIds}. ReportId: {ReportId}", string.Join(", ", request.LinkIds).Replace("\r", " ").Replace("\n", " "), reportId);
        return Result.Success();
    }

     public async Task<Result<JobDeleteErrorResponse>> DeleteAsync(Guid id, CancellationToken cancellationToken)
     {
         var organizationId = currentUser.OrganizationId;
         if (organizationId is null)
         {
             return Result<JobDeleteErrorResponse>.Unauthorized();
         }

         var deletedJob = jobDeletionNotificationService.IsEnabled
             ? await _jobRepository.GetSingleJobAsync(id, organizationId.Value, cancellationToken)
             : null;

         var deleteResult = await _jobRepository.DeleteAsync(id, organizationId.Value, cancellationToken);
         if (deleteResult.Status == JobDeleteRepositoryStatus.NotFound)
         {
             logger.LogWarning("Job delete returned not found. JobId: {JobId}. OrganizationId: {OrganizationId}", id, organizationId.Value);
             return Result<JobDeleteErrorResponse>.NotFound();
         }

         if (deleteResult.Status == JobDeleteRepositoryStatus.BlockedByWorksheets)
         {
             var error = JobDeleteErrorResponse.HasAttachedWorksheets(deleteResult.WorksheetCount);
             logger.LogWarning("Job delete blocked by attached worksheets. JobId: {JobId}. OrganizationId: {OrganizationId}. WorksheetCount: {WorksheetCount}",
                 id, organizationId.Value, deleteResult.WorksheetCount);
             return Conflict(error);
         }

         await InvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
         logger.LogInformation("Job deleted. JobId: {JobId}.", id);

         if (deletedJob is not null)
         {
             await jobDeletionNotificationService.QueueAsync(
                 deletedJob,
                 cancellationToken);
         }

         return Result<JobDeleteErrorResponse>.NoContent();
      }

    private static Result<JobDeleteErrorResponse> Conflict(JobDeleteErrorResponse error) =>
        Result<JobDeleteErrorResponse>.Conflict(error.ToConflictError());

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

    private async Task<Result<JobReportSummaryResponse>> ToSummaryResultAsync(JobReportResponse report, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        var referenceData = organizationId.HasValue
            ? await referenceDataRepository.GetAsync(organizationId.Value, cancellationToken)
            : null;
        var worksheets = await worksheetRepository.ListByJobAsync(report.Id, cancellationToken);
        return Result<JobReportSummaryResponse>.Success(ToSummary(report, referenceData!, worksheets, currentUser));
    }

    private static JobReportSummaryResponse ToSummary(
        JobReportResponse report,
        ReferenceDataResponse referenceData,
        IReadOnlyList<WorksheetResponse> worksheets,
        ICurrentUserContext? user = null) =>
        JobReportSummaryMapper.ToSummary(report, referenceData, worksheets, user);

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
            if (!string.IsNullOrWhiteSpace(workKind?.CustomWorkKind))
            {
                errors.Add(new ValidationError { Identifier = $"{nameof(JobReportResponse.WorkKind)}.{nameof(JobWorkKindResponse.CustomWorkKind)}", ErrorMessage = "Custom work kind requires a work kind." });
            }
        }
        else if (!workKindsByLabel.TryGetValue(normalizedWorkKind, out var workKindDefinition))
        {
            errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = $"Unknown work kind '{normalizedWorkKind}'." });
        }
        else if (!workKindDefinition.RequiresCustomWorkKind && !string.IsNullOrWhiteSpace(workKind?.CustomWorkKind))
        {
            errors.Add(new ValidationError { Identifier = $"{nameof(JobReportResponse.WorkKind)}.{nameof(JobWorkKindResponse.CustomWorkKind)}", ErrorMessage = "Custom work kind is only allowed for work kinds that require custom text." });
        }

        if (workKind?.ClosureFlags is not null)
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

            var isNotCompletedSelected = normalizedClosureFlags.Any(f => f.Equals(ClosureFlagLabels.NotCompleted, StringComparison.OrdinalIgnoreCase));
            var hasIncompatibleWithNotCompleted = normalizedClosureFlags.Any(f =>
                f.Equals(ClosureFlagLabels.Completed, StringComparison.OrdinalIgnoreCase) ||
                f.Equals(ClosureFlagLabels.ReadyForInvoice, StringComparison.OrdinalIgnoreCase));
            if (isNotCompletedSelected && hasIncompatibleWithNotCompleted)
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.ClosureFlags), ErrorMessage = "Ikke færdig kan ikke kombineres med Færdig eller Klar til faktura." });
            }

            var hasOperationMaintenance = normalizedClosureFlags.Any(f =>
                f.Equals(ClosureFlagLabels.OperationMaintenanceInstructions, StringComparison.OrdinalIgnoreCase));
            var hasCompletionStatus = normalizedClosureFlags.Any(f =>
                f.Equals(ClosureFlagLabels.NotCompleted, StringComparison.OrdinalIgnoreCase) ||
                f.Equals(ClosureFlagLabels.Completed, StringComparison.OrdinalIgnoreCase) ||
                f.Equals(ClosureFlagLabels.ReadyForInvoice, StringComparison.OrdinalIgnoreCase));
            if (hasOperationMaintenance && !hasCompletionStatus)
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.ClosureFlags), ErrorMessage = "Drift og vedligeholdelses-instruktioner kræver at der også vælges Ikke færdig, Færdig eller Klar til faktura." });
            }
        }

        return errors;
    }

    private static List<ValidationError> ValidateSearchFilters(
        string? reportNumber, string? customerName, string? customerEmail, string? customerAddress, string? search)
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
        if (search?.Length > 0 && search.Length < 2)
            errors.Add(new() { Identifier = nameof(search), ErrorMessage = "Søgning skal være på mindst 2 tegn." });
        return errors;
    }

    private static JobQuery BuildJobQuery(
        Guid organizationId, List<JobStatus>? statuses,
        string? reportNumber, string? customerName, string? customerEmail, string? customerAddress,
        string? search,
        string? sortBy, string? sortDirection,
        int? limit, int? offset, Guid? currentUserId = null, Guid? assignedToUserId = null)
    {
        var normalizedReportSearch = string.IsNullOrWhiteSpace(reportNumber) ? null : reportNumber.Trim();
        var normalizedNameSearch = string.IsNullOrWhiteSpace(customerName) ? null : customerName.Trim();
        var normalizedEmailSearch = string.IsNullOrWhiteSpace(customerEmail) ? null : customerEmail.Trim();
        var normalizedAddressSearch = string.IsNullOrWhiteSpace(customerAddress) ? null : customerAddress.Trim();
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var normalizedSortBy = string.IsNullOrWhiteSpace(sortBy) ? null : sortBy.Trim();
        var normalizedSortDirection = string.IsNullOrWhiteSpace(sortDirection) ? null : sortDirection.Trim().ToLowerInvariant();

        return new JobQuery(organizationId, statuses, Math.Clamp(limit ?? 50, 1, 200), Math.Max(offset ?? 0, 0),
            currentUserId,
            assignedToUserId,
            normalizedReportSearch, normalizedNameSearch, normalizedEmailSearch, normalizedAddressSearch,
            normalizedSearch,
            normalizedSortBy, normalizedSortDirection);
    }

    /// <summary>
    /// Job list cache keys are HybridCache keys, so once a distributed second level is
    /// registered they are Redis key <em>names</em>: sent to the server in the clear,
    /// printed by <c>redis-cli --scan</c>, and quoted verbatim in provider exceptions.
    /// They therefore carry no customer data in plaintext.
    ///
    /// The key has two halves. What an operator needs in order to read the keyspace stays
    /// readable - organization, viewer, assignment scope, the status set, the ordering and
    /// the page - because each of those is either an opaque identifier or a closed
    /// vocabulary. Everything the query distinguishes, the readable components included,
    /// is then folded into one SHA-256 fingerprint over a length-framed encoding of the
    /// whole <see cref="JobQuery"/>, and that fingerprint is what guarantees two different
    /// queries get two different keys. The readable half is allowed to summarise; the
    /// fingerprint is not.
    /// </summary>
    private static string BuildJobListCacheKey(JobQuery query)
    {
        var statuses = CanonicalStatuses(query.Statuses);

        return $"jobs:list:organization={query.OrganizationId:N}" +
            $":currentUser={query.CurrentUserId?.ToString("N") ?? "none"}" +
            $":assignedTo={query.AssignedToUserId?.ToString("N") ?? "all"}" +
            $":status={StatusKeyPart(statuses)}" +
            $":sort={SortKeyPart(query.SortBy, query.SortDirection)}" +
            $":limit={query.Limit}:offset={query.Offset}" +
            $":filters={FilterPresenceKeyPart(query)}" +
            $":query={JobListQueryFingerprint(query, statuses)}";
    }

    /// <summary>
    /// The repository filters statuses with set semantics (<c>Distinct()</c>, then
    /// <c>Contains</c>), so neither order nor duplicates can change a result set.
    /// Collapsing them therefore buys cache hits without merging two different queries -
    /// and it keeps the readable status component bounded.
    /// </summary>
    private static List<JobStatus> CanonicalStatuses(List<JobStatus>? statuses) =>
        statuses is null || statuses.Count == 0
            ? []
            : statuses.Distinct().OrderBy(status => (int)status).ToList();

    /// <summary>
    /// Status names for the values the enum defines, plus a count of any undefined ones
    /// that model binding let through. Names are a closed vocabulary, so this component
    /// cannot contain the key delimiter, and it stays short however many status values
    /// arrive.
    /// </summary>
    private static string StatusKeyPart(IReadOnlyList<JobStatus> canonicalStatuses)
    {
        if (canonicalStatuses.Count == 0)
        {
            return "all";
        }

        var defined = canonicalStatuses
            .Where(status => Enum.IsDefined(status))
            .Select(status => status.ToString());
        var undefinedCount = canonicalStatuses.Count(status => !Enum.IsDefined(status));

        return string.Join(",", undefinedCount == 0 ? defined : defined.Append($"{undefinedCount}unknown"));
    }

    /// <summary>
    /// A label for the ordering, not the authority on it. The raw sort inputs are in the
    /// fingerprint, so an ordering this method cannot name still gets its own key: if a
    /// sort column is added to the repository and not to <see cref="JobListSortColumns"/>,
    /// the label degrades to "default" and no two orderings merge. Every unrecognised
    /// column falls into the repository's default ordering, which does not consult the
    /// direction either, so the label drops it there.
    /// </summary>
    private static string SortKeyPart(string? sortBy, string? sortDirection)
    {
        if (sortBy is null || !JobListSortColumns.Contains(sortBy, StringComparer.Ordinal))
        {
            return "default";
        }

        return string.Equals(sortDirection, "asc", StringComparison.Ordinal)
            ? $"{sortBy}.asc"
            : $"{sortBy}.desc";
    }

    /// <summary>
    /// Which free-text filters a query applied - field names only, never their values -
    /// so the shape of a query is still legible from the keyspace.
    /// </summary>
    private static string FilterPresenceKeyPart(JobQuery query)
    {
        (string Name, string? Value)[] filters =
        [
            ("report", query.ReportNumber),
            ("name", query.CustomerName),
            ("email", query.CustomerEmail),
            ("address", query.CustomerAddress),
            ("search", query.Search)
        ];

        var applied = filters
            .Where(filter => filter.Value is not null)
            .Select(filter => filter.Name)
            .ToArray();

        return applied.Length == 0 ? "none" : string.Join("+", applied);
    }

    /// <summary>
    /// SHA-256 over a length-framed encoding of every component of the query, truncated to
    /// 128 bits (32 hex characters).
    ///
    /// The framing is what makes the fingerprint unambiguous. Every component is written
    /// either at a fixed width (a Guid, an int, a presence flag) or as a 4-byte UTF-8 byte
    /// count followed by exactly that many bytes, with a count of -1 for null, in a fixed
    /// order. The encoding is therefore parseable, which means it has a left inverse and
    /// is injective: no value - however many colons, equals signs or "none" literals it
    /// contains - can absorb the component after it or impersonate a component boundary.
    /// Two queries share a fingerprint only if every component is byte-identical (statuses
    /// after the canonicalisation above), or if SHA-256 collides on 128 bits.
    /// </summary>
    private static string JobListQueryFingerprint(JobQuery query, IReadOnlyList<JobStatus> canonicalStatuses)
    {
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        AppendGuid(fingerprint, query.OrganizationId);
        AppendOptionalGuid(fingerprint, query.CurrentUserId);
        AppendOptionalGuid(fingerprint, query.AssignedToUserId);

        AppendInt32(fingerprint, canonicalStatuses.Count);
        foreach (var status in canonicalStatuses)
        {
            AppendInt32(fingerprint, (int)status);
        }

        AppendInt32(fingerprint, query.Limit);
        AppendInt32(fingerprint, query.Offset);
        AppendOptionalText(fingerprint, query.ReportNumber);
        AppendOptionalText(fingerprint, query.CustomerName);
        AppendOptionalText(fingerprint, query.CustomerEmail);
        AppendOptionalText(fingerprint, query.CustomerAddress);
        AppendOptionalText(fingerprint, query.Search);
        AppendOptionalText(fingerprint, query.SortBy);
        AppendOptionalText(fingerprint, query.SortDirection);

        return Convert.ToHexString(fingerprint.GetHashAndReset())[..JobListFingerprintHexChars].ToLowerInvariant();
    }

    private static void AppendInt32(IncrementalHash fingerprint, int value)
    {
        Span<byte> framed = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(framed, value);
        fingerprint.AppendData(framed);
    }

    private static void AppendGuid(IncrementalHash fingerprint, Guid value)
    {
        Span<byte> framed = stackalloc byte[16];
        value.TryWriteBytes(framed);
        fingerprint.AppendData(framed);
    }

    private static void AppendOptionalGuid(IncrementalHash fingerprint, Guid? value)
    {
        AppendInt32(fingerprint, value.HasValue ? 1 : 0);
        if (value.HasValue)
        {
            AppendGuid(fingerprint, value.Value);
        }
    }

    private static void AppendOptionalText(IncrementalHash fingerprint, string? value)
    {
        if (value is null)
        {
            AppendInt32(fingerprint, -1);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(fingerprint, bytes.Length);
        fingerprint.AppendData(bytes);
    }
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
    private async Task QueueJobAssignmentNotificationsAsync(
        JobReportResponse primaryJob,
        IReadOnlyList<Guid> createdJobIds,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        foreach (var createdJobId in createdJobIds)
        {
            var job = createdJobId == primaryJob.Id
                ? primaryJob
                : await _jobRepository.GetSingleJobAsync(
                    createdJobId,
                    primaryJob.OrganizationId,
                    cancellationToken);
            if (job is null)
            {
                logger.LogError(
                    "Created job lookup failed before assignment notification. JobId: {JobId}. OrganizationId: {OrganizationId}.",
                    createdJobId,
                    primaryJob.OrganizationId);
                continue;
            }

            var reportNumber = job.ReportNumber ?? "Uden nummer";
            var address = job.DestinationAddress ?? job.Customer?.Address ?? "Ingen adresse angivet";
            foreach (var assignedUser in job.AssignedUsers)
            {
                if (assignedUser.Id == actorId)
                    continue;

                try
                {
                    await notificationService.QueueJobAssignedAsync(
                        assignedUser.Id,
                        assignedUser.DisplayName,
                        job.Id,
                        reportNumber,
                        address,
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    // Job creation is already committed and idempotency must not be
                    // aborted because a secondary notification could not be queued.
                    logger.LogError(
                        exception,
                        "Failed to queue job assignment notification. JobId: {JobId}. UserId: {UserId}.",
                        job.Id,
                        assignedUser.Id);
                }
            }
        }
    }

    private static List<ValidationError> MapValidationErrors(ValidationResult result) =>
        result.Errors
            .Select(e => new ValidationError { Identifier = e.PropertyName, ErrorMessage = e.ErrorMessage })
            .ToList();

    private void LogJobCreated(JobReportResponse job) =>
        logger.LogInformation(
            "Job creation completed. PrimaryJobId: {PrimaryJobId}. CreatedJobCount: {CreatedJobCount}. OrganizationId: {OrganizationId}. Status: {Status}. ReportNumber: {ReportNumber}. WorkKindId: {WorkKindId}. AssignedUserCount: {AssignedUserCount}. InstallationTypeCount: {InstallationTypeCount}.",
            job.Id, job.CreatedJobIds?.Count ?? 1, job.OrganizationId, job.Status, job.ReportNumber, job.WorkKind?.Id, job.AssignedUsers.Count, job.InstallationTypes.Count);

    private void LogJobUpdated(JobReportResponse job) =>
        logger.LogInformation(
            "Job updated. JobId: {JobId}. OrganizationId: {OrganizationId}. Status: {Status}. ReportNumber: {ReportNumber}. WorkKindId: {WorkKindId}. InstallationTypeCount: {InstallationTypeCount}.",
            job.Id, job.OrganizationId, job.Status, job.ReportNumber, job.WorkKind?.Id, job.InstallationTypes.Count);

    public async Task InvalidateJobDetailCacheAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        await InvalidateJobCachesAsync(id, organizationId, cancellationToken);
    }

    public async Task<Result> MarkJobAsSeenAsync(Guid id, string? viewType, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        var userId = currentUser.UserId;

        if (organizationId is null || userId is null)
        {
            return Result.Unauthorized();
        }

        var job = await _jobRepository.GetSingleJobAsync(id, organizationId.Value, cancellationToken);
        if (job is null)
        {
            logger.LogWarning("Job mark-as-seen returned not found. JobId: {JobId}.", id);
            return Result.NotFound();
        }

        await _jobViewRepository.MarkAsViewedAsync(id, userId.Value, viewType ?? JobViewTypes.New, cancellationToken);
        await InvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);

        logger.LogInformation("Job marked as seen. JobId: {JobId}. UserId: {UserId}. ReportNumber: {ReportNumber}", id, userId.Value, job.ReportNumber);
        return Result.Success();
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
