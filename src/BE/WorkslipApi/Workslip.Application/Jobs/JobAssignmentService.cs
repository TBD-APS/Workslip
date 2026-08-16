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

    Task<Result> ValidateSelfAssignmentTargetAsync(
        Guid jobId,
        Guid targetUserId,
        CancellationToken cancellationToken);

    Task<Result> AssignSelfAsync(
        Guid jobId,
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

        var currentJob = await jobs.GetSingleJobAsync(jobId, cancellationToken);
        if (!currentJob.IsSuccess)
        {
            return currentJob;
        }

        if (currentJob.Value.Status == JobStatus.Approved)
        {
            return Result<JobReportSummaryResponse>.Conflict("approved_job_locked");
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

    public async Task<Result> ValidateSelfAssignmentTargetAsync(
        Guid jobId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        if (!JobAssignmentPolicy.CanManageAssignments(currentUser.Role))
            return Result.Forbidden();

        if (targetUserId == Guid.Empty)
            return Result.Invalid(new ValidationError
            {
                Identifier = nameof(targetUserId),
                ErrorMessage = "Vælg en gyldig medarbejder."
            });

        var validation = await validator.ValidateForExistingJobAsync(
            jobId,
            [targetUserId],
            cancellationToken);
        if (validation.Status != JobAssignmentValidationStatus.Valid)
            return MapValidationFailureResult(validation);

        var job = await jobs.GetSingleJobAsync(jobId, cancellationToken);
        if (!job.IsSuccess)
            return MapJobFailure(job);

        if (job.Value.Status == JobStatus.Approved)
            return Result.Conflict("approved_job_locked");

        if (job.Value.AssignedUsers.Any(user => user.Id == targetUserId))
            return Result.Conflict("user_already_assigned");

        return Result.Success();
    }

    public async Task<Result> AssignSelfAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId
            || currentUser.UserId is not Guid userId)
        {
            return Result.Unauthorized();
        }

        if (!JobAssignmentPolicy.CanReceiveAssignment(currentUser.Role))
            return Result.Forbidden();

        var validation = await validator.ValidateForExistingJobAsync(
            jobId,
            [userId],
            cancellationToken);
        if (validation.Status != JobAssignmentValidationStatus.Valid)
            return MapValidationFailureResult(validation);

        var addResult = await assignmentRepository.AddAssignedUserAsync(
            organizationId,
            jobId,
            userId,
            userId,
            cancellationToken);

        switch (addResult)
        {
            case AddAssignedUserResult.NotFound:
                return Result.NotFound();
            case AddAssignedUserResult.Locked:
                return Result.Conflict("approved_job_locked");
            case AddAssignedUserResult.Added:
            case AddAssignedUserResult.AlreadyAssigned:
                break;
            default:
                return Result.Error("job_self_assignment_failed");
        }

        await jobs.InvalidateJobDetailCacheAsync(jobId, organizationId, cancellationToken);
        logger.LogInformation(
            "Job self-assignment completed. JobId: {JobId}. OrganizationId: {OrganizationId}. UserId: {UserId}. Result: {AssignmentResult}.",
            jobId,
            organizationId,
            userId,
            addResult);
        return Result.NoContent();
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

    private static Result MapValidationFailureResult(JobAssignmentValidationResult validation) =>
        validation.Status switch
        {
            JobAssignmentValidationStatus.Unauthorized => Result.Unauthorized(),
            JobAssignmentValidationStatus.JobNotFound => Result.NotFound(),
            JobAssignmentValidationStatus.InvalidAssignee => Result.Invalid(new ValidationError
            {
                Identifier = nameof(AssignJobRequest.UserIds),
                ErrorMessage = validation.ErrorMessage ?? "Ugyldig tildeling."
            }),
            _ => Result.Error("job_assignment_validation_failed")
        };

    private static Result MapJobFailure(Result<JobReportSummaryResponse> result) => result.Status switch
    {
        ResultStatus.Unauthorized => Result.Unauthorized(),
        ResultStatus.Forbidden => Result.Forbidden(),
        ResultStatus.NotFound => Result.NotFound(),
        ResultStatus.Invalid => Result.Invalid(result.ValidationErrors),
        ResultStatus.Conflict => Result.Conflict(result.Errors.FirstOrDefault() ?? "job_conflict"),
        _ => Result.Error(result.Errors.FirstOrDefault() ?? "job_access_failed")
    };
}
