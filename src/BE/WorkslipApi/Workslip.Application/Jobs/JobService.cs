using Ardalis.Result;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Application.Users;
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
    IUserRepository userRepository,
    IWorksheetRepository worksheetRepository,
    HybridCache cache,
    IValidator<CreateJobRequest> createJobValidator,
    IValidator<UpdateJobRequest> updateJobValidator,
    IValidator<ChangeJobStatusRequest> changeJobStatusValidator,
    ICurrentUserContext currentUser,
    ILogger<JobService> logger,
    JobValidationService jobValidationService,
    INotificationService notificationService) : IJobService
{
    private static readonly HybridCacheEntryOptions JobReportCacheOptions = new()
    {
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    private static readonly HybridCacheEntryOptions JobListCacheOptions = new()
    {
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

        var actorId = currentUser.UserId;
        var assignedUserIds = actorId.HasValue && IsJobAssignableRole(currentUser.Role)
            ? [actorId.Value]
            : Array.Empty<Guid>();
        try
        {
            var created = await _jobRepository.CreateAsync(organizationId.Value, request, assignedUserIds, actorId, cancellationToken);
            await InvalidateJobCachesAsync(created.Id, created.OrganizationId, cancellationToken);
            LogJobCreated(created);

            return await ToSummaryResultAsync(created, cancellationToken);
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

        var query = BuildJobQuery(organizationId.Value, statuses, reportNumber, customerName, customerEmail, customerAddress, search, sortBy, sortDirection, limit, offset, currentUser.UserId);

        var cacheKey = BuildJobListCacheKey(query);
        var jobList = await _jobRepository.ListAsync(query, cancellationToken);
        var result = await cache.GetOrCreateAsync(
            cacheKey,
            async token => jobList,
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

        var job = await _jobRepository.GetSingleJobAsync(id, organizationId.Value, cancellationToken);
        var referenceData = await referenceDataRepository.GetAsync(organizationId.Value, cancellationToken);

        if (job is null)
        {
            logger.LogWarning("Job submit returned not found. JobId: {JobId} with orgId {OrgId}.", id, organizationId.Value);
            return Result<JobReportSummaryResponse>.NotFound();
        }

        var isValidResponse = jobValidationService.ValidateSubmitReady(job, referenceData);

        if (!isValidResponse.IsSuccess)
        {
            return isValidResponse;
        }

        return await TransitionAsync(id, request.Status, request.RejectionNote, cancellationToken);
    }

    private async Task<Result<JobReportSummaryResponse>> TransitionAsync(Guid id, JobStatus targetStatus, string? rejectionNote, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportSummaryResponse>.Unauthorized();
        }

        var actorId = currentUser.UserId;
        var transition = await _jobRepository.TransitionAsync(id, organizationId.Value, targetStatus, actorId, rejectionNote, cancellationToken);
        if (transition is null)
        {
            logger.LogWarning("Job transition returned not found. JobId: {JobId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
                id,
                targetStatus,
                actorId);

            return Result<JobReportSummaryResponse>.NotFound();
        }

        var report = transition.Report;
        if (!transition.Changed)
        {
            logger.LogInformation("Duplicate job transition ignored. JobId: {JobId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
                report.Id,
                targetStatus,
                actorId);
            return await ToSummaryResultAsync(report, cancellationToken);
        }

        await InvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
        logger.LogInformation("Job transitioned. JobId: {JobId}. OrganizationId: {OrganizationId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
            report.Id,
            report.OrganizationId,
            targetStatus,
            actorId);

        var address = report.DestinationAddress ?? report.Customer?.Address ?? "Ingen adresse angivet";
        var reportNumber = report.ReportNumber ?? "Uden nummer";

        if (targetStatus == JobStatus.InReview)
        {
            var users = await userRepository.GetByOrganizationIdAsync(organizationId.Value, 1000, 0, null, null, null, cancellationToken);
            var admins = users.Where(x => x.Role == Roles.Admin);
            
            foreach (var admin in admins)
            {
                logger.LogInformation("Sending review notification to {UserName} with id {UserId}", admin.DisplayName, admin.Id);
                
                if (admin.Id == currentUser.UserId) 
                    continue;

                await notificationService.QueueJobReadyForReviewAsync(admin.Id, admin.DisplayName, report.Id, reportNumber, address, cancellationToken);
                logger.LogInformation("Sent review notification to {UserName} with id {UserId}", admin.DisplayName, admin.Id);
            }
        }
        else if (targetStatus == JobStatus.Rejected)
        {
            var events = await _jobRepository.GetEventsAsync(id, organizationId.Value, 100, 0, cancellationToken);
            var submitterEvent = events?.FirstOrDefault(e =>
                e.ActorId is not null
                && e.Changes.Any(c => c.PropertyName == "Status" && c.After == JobStatus.InReview.ToString()));

            if (submitterEvent?.ActorId is Guid submitterId)
            {
                await assignmentRepository.AssignAsync(report.Id, organizationId.Value, [submitterId], actorId, cancellationToken);
                report = await _jobRepository.GetSingleJobAsync(id, organizationId.Value, cancellationToken) ?? report;
                logger.LogInformation("Job reassigned to submitter on rejection. JobId: {JobId}. SubmitterId: {SubmitterId}.", id, submitterId);
            }
            else
            {
                logger.LogWarning("Could not find submitter for rejected job. JobId: {JobId}. Falling back to current assignees.", id);
            }

            foreach (var assignedUser in report.AssignedUsers)
            {
                if (assignedUser.Id == currentUser.UserId) continue;
                await notificationService.QueueJobDeniedAsync(assignedUser.Id, assignedUser.DisplayName, report.Id, reportNumber, address, rejectionNote, cancellationToken);
            }
        }
        else if (targetStatus == JobStatus.Approved)
        {
            foreach (var assignedUser in report.AssignedUsers)
            {
                if (assignedUser.Id == currentUser.UserId) 
                    continue;
                
                await notificationService.QueueJobCompletedAsync(assignedUser.Id, assignedUser.DisplayName, report.Id, reportNumber, address, cancellationToken);
            }

            await _jobViewRepository.MarkAsViewedAsync(id, currentUser.UserId!.Value, "New", cancellationToken);
        }

        return await ToSummaryResultAsync(report, cancellationToken);
    }

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

        logger.LogInformation("Job links deleted. LinkIds: {LinkIds}. ReportId: {ReportId}", request.LinkIds, reportId);
        return Result.Success();
    }

     public async Task<Result<JobDeleteErrorResponse>> DeleteAsync(Guid id, CancellationToken cancellationToken)
     {
         var organizationId = currentUser.OrganizationId;
         if (organizationId is null)
         {
             return Result<JobDeleteErrorResponse>.Unauthorized();
         }

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

           var address = job.DestinationAddress ?? job.Customer?.Address ?? "Ingen adresse angivet";
           var reportNumber = job.ReportNumber ?? "Uden nummer";
           foreach (var userId in userIds)
           {
               if (userId == currentUser.UserId) 
                 continue;
                 
               var assignedUser = await userRepository.GetByIdAsync(userId, cancellationToken);
               var recipientName = assignedUser?.DisplayName ?? "Bruger";
               await notificationService.QueueJobAssignedAsync(userId, recipientName, jobId, reportNumber, address, cancellationToken);
           }

           if (userIds.Count == 0)
           {
               var allUsers = await userRepository.GetByOrganizationIdAsync(organizationId.Value, 1000, 0, null, null, null, cancellationToken);
               var admins = allUsers.Where(u => u.Role == Roles.Admin);

               foreach (var admin in admins)
               {
                   if (admin.Id == currentUser.UserId)
                       continue;

                   await notificationService.QueueJobUnassignedAsync(admin.Id, admin.DisplayName, jobId, reportNumber, address, cancellationToken);
               }
           }

          return await ToSummaryResultAsync(job, cancellationToken);
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
        ICurrentUserContext? user = null)
    {
        var isRegularUser = user != null && user.Role == Roles.User;
        var filteredWorksheets = isRegularUser
            ? worksheets.Where(w => w.UserId == user!.UserId).ToList()
            : worksheets;

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

        var totalHours = filteredWorksheets.Sum(w => w.HoursWorked);
        var totalOverLay = filteredWorksheets.Count(w => w.SleptOnJob);

        var customerSnapshot = new CustomerSnapshotResponse(
            report.Customer?.Name,
            report.Customer?.Email,
            report.Customer?.Phone,
            report.Customer?.Address,
            report.Customer?.ContactPerson);

        return new(
            report.Id,
            report.OrganizationId,
            report.OrganizationName,
            report.OrganizationCvr,
            report.ReportNumber,
            report.Status,
            report.Customer?.CustomerId,
            customerSnapshot,
            report.DestinationAddress,
            report.DestinationZipCode,
            report.DestinationCity,
            report.JobType.ToString(),
            new JobReportSummaryWorkResponse(
                report.WorkKind,
                report.InstallationTypes,
                closureFlags!,
                report.Remarks),
            new JobReportSummaryObservationResponse(
                report.TaskDescription,
                report.CustomerObservations,
                report.TechnicalObservations),
            Array.Empty<ControlInstallationTypeResponse>(),
            report.Links,
            report.CreatedAt,
            report.UpdatedAt,
            report.SubmittedAt,
            report.AssignedUsers,
            filteredWorksheets,
            totalHours,
            totalOverLay,
            report.SoftDeleted,
            report.RejectionNote);
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
        int? limit, int? offset, Guid? currentUserId = null)
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
            normalizedReportSearch, normalizedNameSearch, normalizedEmailSearch, normalizedAddressSearch,
            normalizedSearch,
            normalizedSortBy, normalizedSortDirection);
    }

    private static string BuildJobListCacheKey(JobQuery query)
    {
        var statusKey = query.Statuses is not null && query.Statuses.Count > 0
            ? string.Join(",", query.Statuses.OrderBy(x => x).Select(x => x.ToString()))
            : "all";
    
        return $"jobs:list:organization={query.OrganizationId:N}:status={statusKey}" +
            $":reportNumber={query.ReportNumber ?? "none"}:customerName={query.CustomerName ?? "none"}" +
            $":customerEmail={query.CustomerEmail ?? "none"}:customerAddress={query.CustomerAddress ?? "none"}" +
            $":search={query.Search ?? "none"}" +
            $":sortBy={query.SortBy ?? "default"}:sortDirection={query.SortDirection ?? "default"}" +
            $":limit={query.Limit}:offset={query.Offset}";
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
    private async Task<Result<JobReportSummaryResponse>?> ValidateAssignedUsersExistAsync(
        IReadOnlyList<Guid>? userIds, Guid jobId, Guid organizationId, CancellationToken cancellationToken)
    {
        if (userIds is null || userIds.Count == 0) 
            return null;

        foreach (var userId in userIds)
        {
            var assignedUser = await userRepository.GetByIdAsync(userId, cancellationToken);
            if (assignedUser is not null
                && assignedUser.OrganizationId == organizationId
                && IsJobAssignableRole(assignedUser.Role))
                continue;

            logger.LogError("Job assignment validation failed. JobId: {JobId}. OrganizationId: {OrganizationId}. InvalidAssignedUserId: {InvalidAssignedUserId}.",
                jobId, organizationId, userId);

            return Result<JobReportSummaryResponse>.Invalid([new ValidationError
            {
                Identifier = nameof(AssignJobRequest.UserIds),
                ErrorMessage = "Sager kan kun tildeles brugere eller administratorer i samme organisation."
            }]);
        }

        return null;
    }

    private static bool IsJobAssignableRole(string? role) =>
        string.Equals(role, Roles.User, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, Roles.Admin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase);

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

        await _jobViewRepository.MarkAsViewedAsync(id, userId.Value, viewType ?? "New", cancellationToken);
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
