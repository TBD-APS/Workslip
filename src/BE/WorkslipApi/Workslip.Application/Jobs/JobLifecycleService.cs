using Ardalis.Result;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Application.Notifications;
using Workslip.Application.Worksheets;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

/// <summary>
/// Coordinates job status changes after the product-facing authorization boundary.
/// Persistence remains the lifecycle source of truth. Post-commit cache and notification
/// failures must not make an already-persisted status transition look unsuccessful to the caller.
/// </summary>
public sealed class JobLifecycleService(
    IJobRepository jobRepository,
    IJobViewRepository jobViewRepository,
    IAssignmentRepository assignmentRepository,
    IReferenceDataRepository referenceDataRepository,
    IWorksheetRepository worksheetRepository,
    HybridCache cache,
    IValidator<ChangeJobStatusRequest> changeJobStatusValidator,
    ICurrentUserContext currentUser,
    ILogger<JobService> logger,
    JobValidationService jobValidationService,
    INotificationService notificationService)
{
    public async Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(
        Guid id,
        ChangeJobStatusRequest request,
        CancellationToken cancellationToken)
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

        var job = await jobRepository.GetSingleJobAsync(id, organizationId.Value, cancellationToken);

        if (job is null)
        {
            logger.LogWarning(
                "Job transition returned not found before lifecycle validation. JobId: {JobId} with orgId {OrgId}.",
                id,
                organizationId.Value);
            return Result<JobReportSummaryResponse>.NotFound();
        }

        // Submit readiness belongs only to actual entry into review. Reviewer
        // decisions and same-status retries operate on an already-submitted snapshot
        // and must not become impossible because reference data or rules changed later.
        if (request.Status == JobStatus.InReview && job.Status != JobStatus.InReview)
        {
            var referenceData = await referenceDataRepository.GetAsync(organizationId.Value, cancellationToken);
            var submitReady = jobValidationService.ValidateSubmitReady(job, referenceData);
            if (!submitReady.IsSuccess)
            {
                return submitReady;
            }
        }

        return await TransitionAsync(id, request.Status, request.RejectionNote, cancellationToken);
    }

    private async Task<Result<JobReportSummaryResponse>> TransitionAsync(
        Guid id,
        JobStatus targetStatus,
        string? rejectionNote,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportSummaryResponse>.Unauthorized();
        }

        var actorId = currentUser.UserId;
        if (actorId is null)
        {
            return Result<JobReportSummaryResponse>.Unauthorized();
        }

        var transition = await jobRepository.TransitionAsync(
            id,
            organizationId.Value,
            targetStatus,
            actorId,
            rejectionNote,
            cancellationToken);
        if (transition is null)
        {
            logger.LogWarning(
                "Job transition returned not found. JobId: {JobId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
                id,
                targetStatus,
                actorId);

            return Result<JobReportSummaryResponse>.NotFound();
        }

        var report = transition.Report;
        CorrectionRoutingResult? correctionRouting = null;
        // Returning an approved job for correction uses Reopened, but needs the same
        // submitter assignment and correction notification as a rejection.
        if (targetStatus is JobStatus.Rejected or JobStatus.Reopened)
        {
            correctionRouting = await RouteJobForCorrectionAsync(
                transition,
                report,
                organizationId.Value,
                actorId.Value,
                cancellationToken);
            report = correctionRouting.Report;
        }

        var address = report.DestinationAddress ?? report.Customer?.Address ?? "Ingen adresse angivet";
        var reportNumber = report.ReportNumber ?? "Uden nummer";

        if (!transition.Changed)
        {
            // A previous correction attempt can have persisted the status before the
            // submitter reassignment completed. Same-status retries therefore repair
            // that routing instead of becoming a no-op.
            if (correctionRouting is { RoutingChanged: true } recoveredRouting)
            {
                await TryInvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
                await QueueCorrectionNotificationsBestEffortAsync(
                    recoveredRouting.Recipients,
                    report,
                    reportNumber,
                    address,
                    rejectionNote ?? report.RejectionNote,
                    actorId.Value,
                    cancellationToken);
            }

            logger.LogInformation(
                "Duplicate job transition ignored after lifecycle reconciliation. JobId: {JobId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
                report.Id,
                targetStatus,
                actorId);
            return await ToSummaryResultAsync(report, cancellationToken);
        }

        await TryInvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
        logger.LogInformation(
            "Job transitioned. JobId: {JobId}. OrganizationId: {OrganizationId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
            report.Id,
            report.OrganizationId,
            targetStatus,
            actorId);

        if (targetStatus == JobStatus.InReview)
        {
            var admins = await assignmentRepository.GetOrganizationAdminsAsync(
                organizationId.Value,
                cancellationToken);

            var queuedNotificationCount = 0;
            foreach (var admin in admins)
            {
                if (admin.Id == currentUser.UserId)
                    continue;

                await notificationService.QueueJobReadyForReviewAsync(
                    admin.Id,
                    admin.DisplayName,
                    report.Id,
                    reportNumber,
                    address,
                    cancellationToken);
                queuedNotificationCount++;
            }

            logger.LogDebug(
                "Queued job review notifications. JobId {JobId}. RecipientCount {RecipientCount}.",
                report.Id,
                queuedNotificationCount);
        }
        else if (correctionRouting is { } routing)
        {
            // Reassignment is part of correction routing and must succeed. Notification
            // queueing is post-commit delivery work and is deliberately best-effort:
            // a queue outage must not turn a persisted rejection into a false API failure.
            await TryInvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
            await QueueCorrectionNotificationsBestEffortAsync(
                routing.Recipients,
                report,
                reportNumber,
                address,
                rejectionNote,
                actorId.Value,
                cancellationToken);
        }
        else if (targetStatus == JobStatus.Approved)
        {
            foreach (var assignedUser in report.AssignedUsers)
            {
                if (assignedUser.Id == currentUser.UserId)
                    continue;

                await notificationService.QueueJobCompletedAsync(
                    assignedUser.Id,
                    assignedUser.DisplayName,
                    report.Id,
                    reportNumber,
                    address,
                    cancellationToken);
            }

            await jobViewRepository.MarkAsViewedAsync(
                id,
                actorId.Value,
                JobViewTypes.Completed,
                cancellationToken);
        }

        return await ToSummaryResultAsync(report, cancellationToken);
    }

    private async Task<CorrectionRoutingResult> RouteJobForCorrectionAsync(
        JobTransitionResult transition,
        JobReportResponse report,
        Guid organizationId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AssignedUserResponse> recipients = [];
        var routingChanged = false;

        if (transition.SubmittedByUserId is Guid submitterId)
        {
            recipients = await assignmentRepository.GetAssignedUsersByIdsAsync(
                organizationId,
                [submitterId],
                cancellationToken);

            if (recipients.Count == 1)
            {
                var wasAlreadyRouted =
                    report.AssignedUsers.Count == 1 &&
                    report.AssignedUsers[0].Id == submitterId;

                await assignmentRepository.AssignAsync(
                    report.Id,
                    organizationId,
                    [submitterId],
                    actorId,
                    cancellationToken);

                report = await jobRepository.GetSingleJobAsync(
                    report.Id,
                    organizationId,
                    cancellationToken) ?? report;
                routingChanged = !wasAlreadyRouted;

                logger.LogInformation(
                    "Job routed to persisted submitter for correction. JobId: {JobId}. SubmitterId: {SubmitterId}. RoutingChanged: {RoutingChanged}.",
                    report.Id,
                    submitterId,
                    routingChanged);
            }
            else
            {
                logger.LogWarning(
                    "Persisted submitter was not found in the job organization. JobId: {JobId}. SubmitterId: {SubmitterId}. OrganizationId: {OrganizationId}.",
                    report.Id,
                    submitterId,
                    organizationId);
            }
        }

        if (recipients.Count == 0)
        {
            recipients = report.AssignedUsers
                .Where(user => user.Id != actorId)
                .DistinctBy(user => user.Id)
                .ToArray();
            logger.LogWarning(
                "Job returned for correction has no valid persisted submitter. Falling back to current assignees. JobId: {JobId}. RecipientCount: {RecipientCount}.",
                report.Id,
                recipients.Count);
        }

        return new CorrectionRoutingResult(report, recipients, routingChanged);
    }

    private async Task QueueCorrectionNotificationsBestEffortAsync(
        IReadOnlyList<AssignedUserResponse> recipients,
        JobReportResponse report,
        string reportNumber,
        string address,
        string? rejectionNote,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        foreach (var recipient in recipients)
        {
            if (recipient.Id == actorId)
                continue;

            try
            {
                await notificationService.QueueJobDeniedAsync(
                    recipient.Id,
                    recipient.DisplayName,
                    report.Id,
                    reportNumber,
                    address,
                    rejectionNote,
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    exception,
                    "Job correction status persisted but correction notification could not be queued. JobId: {JobId}. RecipientId: {RecipientId}.",
                    report.Id,
                    recipient.Id);
            }
        }
    }

    private async Task<Result<JobReportSummaryResponse>> ToSummaryResultAsync(
        JobReportResponse report,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        var referenceData = organizationId.HasValue
            ? await referenceDataRepository.GetAsync(organizationId.Value, cancellationToken)
            : null;
        var worksheets = await worksheetRepository.ListByJobAsync(report.Id, cancellationToken);

        return Result<JobReportSummaryResponse>.Success(
            JobReportSummaryMapper.ToSummary(report, referenceData!, worksheets, currentUser));
    }

    private async Task TryInvalidateJobCachesAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveByTagAsync(JobListTag(organizationId), cancellationToken);
            await cache.RemoveByTagAsync(JobReportTag(id, organizationId), cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                exception,
                "Job transition persisted but cache invalidation failed. JobId: {JobId}. OrganizationId: {OrganizationId}.",
                id,
                organizationId);
        }
    }

    private sealed record CorrectionRoutingResult(
        JobReportResponse Report,
        IReadOnlyList<AssignedUserResponse> Recipients,
        bool RoutingChanged);

    private static List<ValidationError> MapValidationErrors(ValidationResult result) =>
        result.Errors
            .Select(e => new ValidationError
            {
                Identifier = e.PropertyName,
                ErrorMessage = e.ErrorMessage
            })
            .ToList();

    private static string JobReportTag(Guid id, Guid organizationId) =>
        $"jobs:detail:{organizationId:N}:{id:N}";

    private static string JobListTag(Guid organizationId) =>
        $"jobs:list:{organizationId:N}";
}
