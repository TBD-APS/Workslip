using Ardalis.Result;
using FluentValidation.Results;
using Workslip.Application.Auth;
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
    IJobService jobs,
    ICurrentUserContext currentUser) : IJobAssignmentService
{
    public async Task<Result<JobReportSummaryResponse>> AssignAsync(
        Guid jobId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (!IsAssignmentManager(currentUser.Role))
        {
            return Result<JobReportSummaryResponse>.Forbidden();
        }

        var validation = await validator.ValidateForExistingJobAsync(
            jobId,
            userIds,
            cancellationToken);

        return validation.Status switch
        {
            JobAssignmentValidationStatus.Valid => await jobs.AssignAsync(jobId, userIds, cancellationToken),
            JobAssignmentValidationStatus.Unauthorized => Result<JobReportSummaryResponse>.Unauthorized(),
            JobAssignmentValidationStatus.JobNotFound => Result<JobReportSummaryResponse>.NotFound(),
            JobAssignmentValidationStatus.InvalidAssignee => Result<JobReportSummaryResponse>.Invalid([
                new ValidationError
                {
                    PropertyName = nameof(AssignJobRequest.UserIds),
                    ErrorMessage = validation.ErrorMessage ?? "Ugyldig tildeling."
                }
            ]),
            _ => Result<JobReportSummaryResponse>.Error("job_assignment_validation_failed")
        };
    }

    private static bool IsAssignmentManager(string? role) =>
        string.Equals(role, Roles.Admin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase);
}
