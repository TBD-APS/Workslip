using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Application.Notifications;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

public interface IJobAssignmentService
{
    Task<Result<JobReportSummaryResponse>> AssignAsync(
        Guid jobId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken);
}

public sealed class JobAssignmentService(
    IJobAssignmentValidator validator,
    IAssignmentRepository assignmentRepository,
    IJobService jobs,
    ICurrentUserContext currentUser,
    INotificationService notificationService,
    ILogger<JobAssignmentService> logger) : IJobAssignmentService
{
    public async Task<Result<JobReportSummaryResponse>> AssignAsync(
        Guid jobId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (!JobAssignmentPolicy.CanManageAssignments(currentUser.Role))
        {
            return Result<JobReportSummaryResponse>.Forbidden();
        }

        var validation = await validator.ValidateForExistingJobAsync(
            jobId,
            userIds,
            cancellationToken);

        if (validation.Status != JobAssignmentValidationStatus.Valid)
        {
            return MapValidationFailure(validation);
        }

        if (currentUser.OrganizationId is not Guid organizationId)
        {
            return Result<JobReportSummaryResponse>.Unauthorized();
        }

        await assignmentRepository.AssignAsync(
            jobId,
            organizationId,
            userIds,
            currentUser.UserId,
            cancellationToken);

        await jobs.InvalidateJobDetailCacheAsync(jobId, organizationId, cancellationToken);

        var jobResult = await jobs.GetSingleJobAsync(jobId, cancellationToken);
        if (!jobResult.IsSuccess)
        {
            return jobResult;
        }

        var job = jobResult.Value;
        var reportNumber = job.ReportNumber ?? "Uden nummer";
        var address = job.DestinationAddress ?? job.CustomerSnapshot.Address ?? "Ingen adresse angivet";

        if (userIds.Count == 0)
        {
            var admins = await assignmentRepository.GetOrganizationAdminsAsync(organizationId, cancellationToken);
            foreach (var admin in admins)
            {
                if (admin.Id == currentUser.UserId)
                    continue;

                await notificationService.QueueJobUnassignedAsync(
                    admin.Id,
                    admin.DisplayName,
                    jobId,
                    reportNumber,
                    address,
                    cancellationToken);
            }
        }
        else
        {
            var recipients = job.AssignedUsers.ToDictionary(user => user.Id);
            foreach (var userId in userIds.Distinct())
            {
                if (userId == currentUser.UserId)
                    continue;

                if (!recipients.TryGetValue(userId, out var recipient))
                {
                    logger.LogWarning(
                        "Validated job assignee was missing after assignment. JobId: {JobId}. OrganizationId: {OrganizationId}. UserId: {UserId}.",
                        jobId,
                        organizationId,
                        userId);
                    continue;
                }

                await notificationService.QueueJobAssignedAsync(
                    recipient.Id,
                    recipient.DisplayName,
                    jobId,
                    reportNumber,
                    address,
                    cancellationToken);
            }
        }

        logger.LogInformation(
            "Job assignment completed. JobId: {JobId}. OrganizationId: {OrganizationId}. AssignedUserCount: {AssignedUserCount}.",
            jobId,
            organizationId,
            userIds.Distinct().Count());

        return jobResult;
    }

    private static Result<JobReportSummaryResponse> MapValidationFailure(JobAssignmentValidationResult validation) =>
        validation.Status switch
        {
            JobAssignmentValidationStatus.Unauthorized => Result<JobReportSummaryResponse>.Unauthorized(),
            JobAssignmentValidationStatus.JobNotFound => Result<JobReportSummaryResponse>.NotFound(),
            JobAssignmentValidationStatus.InvalidAssignee => Result<JobReportSummaryResponse>.Invalid([
                new ValidationError
                {
                    Identifier = nameof(AssignJobRequest.UserIds),
                    ErrorMessage = validation.ErrorMessage ?? "Ugyldig tildeling."
                }
            ]),
            _ => Result<JobReportSummaryResponse>.Error("job_assignment_validation_failed")
        };
}
