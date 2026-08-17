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
/// Persistence, cache invalidation and notification ordering intentionally match the
/// previous JobService behavior; durability/outbox semantics are outside this seam.
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
        var referenceData = await referenceDataRepository.GetAsync(organizationId.Value, cancellationToken);

        if (job is null)
        {
            logger.LogWarning(
                "Job submit returned not found. JobId: {JobId} with orgId {OrgId}.",
                id,
                organizationId.Value);
            return Result<JobReportSummaryResponse>.NotFound();
        }

        var isValidResponse = jobValidationService.ValidateSubmitReady(job, referenceData);
        if (!isValidResponse.IsSuccess)
        {
            return isValidResponse;
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
        if (!transition.Changed)
        {
            logger.LogInformation(
                "Duplicate job transition ignored. JobId: {JobId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
                report.Id,
                targetStatus,
                actorId);
            return await ToSummaryResultAsync(report, cancellationToken);
        }

        await InvalidateJobCachesAsync(id, organizationId.Value, cancellationToken);
        logger.LogInformation(
            "Job transitioned. JobId: {JobId}. OrganizationId: {OrganizationId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
            report.Id,
            report.OrganizationId,
            targetStatus,
            actorId);

        var address = report.DestinationAddress ?? report.Customer?.Address ?? "Ingen adresse angivet";
        var reportNumber = report.ReportNumber ?? "Uden nummer";

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
        else if (targetStatus == JobStatus.Rejected)
        {
            IReadOnlyList<AssignedUserResponse> recipients = [];

            if (transition.SubmittedByUserId is Guid submitterId)
            {
                recipients = await assignmentRepository.GetAssignedUsersByIdsAsync(
                    organizationId.Value,
                    [submitterId],
                    cancellationToken);

                if (recipients.Count == 1)
                {
                    await assignmentRepository.AssignAsync(
                        report.Id,
                        organizationId.Value,
                        [submitterId],
                        actorId,
                        cancellationToken);
                    report = await jobRepository.GetSingleJobAsync(
                        id,
                        organizationId.Value,
                        cancellationToken) ?? report;
                    logger.LogInformation(
                        "Job reassigned to persisted submitter on rejection. JobId: {JobId}. SubmitterId: {SubmitterId}.",
                        id,
                        submitterId);
                }
                else
                {
                    logger.LogWarning(
                        "Persisted submitter was not found in the job organization. JobId: {JobId}. SubmitterId: {SubmitterId}. OrganizationId: {OrganizationId}.",
                        id,
                        submitterId,
                        organizationId.Value);
                }
            }

            if (recipients.Count == 0)
            {
                recipients = report.AssignedUsers
                    .Where(user => user.Id != actorId)
                    .DistinctBy(user => user.Id)
                    .ToArray();
                logger.LogWarning(
                    "Rejected job has no valid persisted submitter. Falling back to current assignees. JobId: {JobId}. RecipientCount: {RecipientCount}.",
                    id,
                    recipients.Count);
            }

            foreach (var recipient in recipients)
            {
                if (recipient.Id == actorId)
                    continue;

                await notificationService.QueueJobDeniedAsync(
                    recipient.Id,
                    recipient.DisplayName,
                    report.Id,
                    reportNumber,
                    address,
                    rejectionNote,
                    cancellationToken);
            }
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

    private async Task InvalidateJobCachesAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        await cache.RemoveByTagAsync(JobListTag(organizationId), cancellationToken);
        await cache.RemoveByTagAsync(JobReportTag(id, organizationId), cancellationToken);
    }

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
