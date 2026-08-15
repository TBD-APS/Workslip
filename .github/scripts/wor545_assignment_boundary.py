from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, content: str) -> None:
    (ROOT / path).write_text(content, encoding="utf-8")


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return source.replace(old, new, 1)


# JobService: remove the duplicate assignment use-case and direct Users-module dependency.
job_service_path = "src/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs"
source = read(job_service_path)
source = replace_once(source, "using Workslip.Application.Users;\n", "", "JobService Users import")
source = replace_once(source, "    IUserRepository userRepository,\n", "", "JobService IUserRepository constructor dependency")
source = replace_once(
    source,
    "            var users = await userRepository.GetByOrganizationIdAsync(organizationId.Value, 1000, 0, null, null, null, cancellationToken);\n            var admins = users.Where(x => x.Role == Roles.Admin);",
    "            var admins = await assignmentRepository.GetOrganizationAdminsAsync(organizationId.Value, cancellationToken);",
    "JobService review admin recipient lookup",
)
assign_start = source.index("     public async Task<Result<JobReportSummaryResponse>> AssignAsync")
assign_end = source.index("    private async Task<Result<JobReportSummaryResponse>> ToSummaryResultAsync", assign_start)
source = source[:assign_start] + source[assign_end:]
validation_start = source.index("    private async Task<Result<JobReportSummaryResponse>?> ValidateAssignedUsersExistAsync")
validation_end = source.index("    private async Task QueueDuplicatedJobAssignmentNotificationsAsync", validation_start)
source = source[:validation_start] + source[validation_end:]
write(job_service_path, source)

# IJobService no longer exposes assignment; assignment has its own application service.
i_job_service_path = "src/BE/WorkslipApi/Workslip.Application/Jobs/IJobService.cs"
source = read(i_job_service_path)
source = replace_once(
    source,
    "        Task<Result<JobReportSummaryResponse>> AssignAsync(Guid jobId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken);\n",
    "",
    "IJobService AssignAsync",
)
write(i_job_service_path, source)

# AuthorizedJobService no longer forwards an assignment operation it does not own.
authorized_path = "src/BE/WorkslipApi/Workslip.Application/Jobs/AuthorizedJobService.cs"
source = read(authorized_path)
old = """    public Task<Result<JobReportSummaryResponse>> AssignAsync(
        Guid jobId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken) =>
        inner.AssignAsync(jobId, userIds, cancellationToken);

"""
source = replace_once(source, old, "", "AuthorizedJobService AssignAsync forwarding")
write(authorized_path, source)

# Keep recipient lookups behind the Jobs-owned assignment port instead of importing Users.
assignment_repo_path = "src/BE/WorkslipApi/Workslip.Application/Jobs/IAssignmentRepository.cs"
source = read(assignment_repo_path)
source = replace_once(
    source,
    "    Task<IReadOnlyList<AssignedUserResponse>> GetAssignedUsersByIdsAsync(Guid organizationId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken);\n",
    "    Task<IReadOnlyList<AssignedUserResponse>> GetAssignedUsersByIdsAsync(Guid organizationId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken);\n    Task<IReadOnlyList<AssignedUserResponse>> GetOrganizationAdminsAsync(Guid organizationId, CancellationToken cancellationToken);\n",
    "IAssignmentRepository admin recipients",
)
write(assignment_repo_path, source)

# Infrastructure implements the Jobs-owned recipient projection directly.
ef_assignment_path = "src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfAssignmentRepository.cs"
source = read(ef_assignment_path)
marker = "    public Task AddAssignedUsersAsync(\n"
admin_method = """    public async Task<IReadOnlyList<AssignedUserResponse>> GetOrganizationAdminsAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.OrganizationId == organizationId && user.Role == Roles.Admin)
            .OrderBy(user => user.DisplayName)
            .Select(user => new AssignedUserResponse(user.Id, user.DisplayName))
            .ToArrayAsync(cancellationToken);
    }

"""
source = replace_once(source, marker, admin_method + marker, "EfAssignmentRepository admin recipient projection")
write(ef_assignment_path, source)

# Assignment validation, mutation, cache invalidation and assignment notifications now have one owner.
job_assignment_service_path = "src/BE/WorkslipApi/Workslip.Application/Jobs/JobAssignmentService.cs"
write(job_assignment_service_path, """using Ardalis.Result;
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
""")

print("WOR-545 assignment boundary refactor applied")
