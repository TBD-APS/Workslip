using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

public interface IJobAssignmentService
{
    Task<Result<IReadOnlyList<JobAssignmentCandidateResponse>>> GetDefaultCandidatesAsync(
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<JobAssignmentCandidateResponse>>> GetCandidatesForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken);

    Task<Result<JobReportSummaryResponse>> AssignAsync(
        Guid jobId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken);
}

public sealed class JobAssignmentService(
    IJobAssignmentValidator validator,
    IJobAssignmentScopeRepository repository,
    IJobService jobs,
    ICurrentUserContext currentUser) : IJobAssignmentService
{
    public async Task<Result<IReadOnlyList<JobAssignmentCandidateResponse>>> GetDefaultCandidatesAsync(
        CancellationToken cancellationToken)
    {
        if (!JobAssignmentPolicy.CanManageAssignments(currentUser.Role))
        {
            return Result<IReadOnlyList<JobAssignmentCandidateResponse>>.Forbidden();
        }

        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<IReadOnlyList<JobAssignmentCandidateResponse>>.Unauthorized();
        }

        var filialId = await repository.GetDefaultFilialIdAsync(organizationId.Value, cancellationToken);
        if (filialId is null)
        {
            return Result<IReadOnlyList<JobAssignmentCandidateResponse>>.Error("default_filial_not_found");
        }

        return Result<IReadOnlyList<JobAssignmentCandidateResponse>>.Success(
            await GetCandidatesAsync(organizationId.Value, filialId.Value, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<JobAssignmentCandidateResponse>>> GetCandidatesForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (!JobAssignmentPolicy.CanManageAssignments(currentUser.Role))
        {
            return Result<IReadOnlyList<JobAssignmentCandidateResponse>>.Forbidden();
        }

        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<IReadOnlyList<JobAssignmentCandidateResponse>>.Unauthorized();
        }

        var filialId = await repository.GetJobFilialIdAsync(organizationId.Value, jobId, cancellationToken);
        if (filialId is null)
        {
            return Result<IReadOnlyList<JobAssignmentCandidateResponse>>.NotFound();
        }

        return Result<IReadOnlyList<JobAssignmentCandidateResponse>>.Success(
            await GetCandidatesAsync(organizationId.Value, filialId.Value, cancellationToken));
    }

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

        return validation.Status switch
        {
            JobAssignmentValidationStatus.Valid => await jobs.AssignAsync(jobId, userIds, cancellationToken),
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

    private async Task<IReadOnlyList<JobAssignmentCandidateResponse>> GetCandidatesAsync(
        Guid organizationId,
        Guid filialId,
        CancellationToken cancellationToken)
    {
        var users = await repository.GetAssignableUsersAsync(
            organizationId,
            filialId,
            cancellationToken);

        return users
            .Where(user => JobAssignmentPolicy.CanReceiveAssignmentInFilial(
                user.Role,
                user.FilialId,
                filialId))
            .Select(user => new JobAssignmentCandidateResponse(user.Id, user.DisplayName))
            .ToArray();
    }
}
