using Workslip.Application.Auth;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

public enum JobAssignmentValidationStatus
{
    Valid,
    Unauthorized,
    JobNotFound,
    InvalidAssignee
}

public sealed record JobAssignmentValidationResult(
    JobAssignmentValidationStatus Status,
    string? ErrorMessage = null)
{
    public static JobAssignmentValidationResult Valid() => new(JobAssignmentValidationStatus.Valid);
    public static JobAssignmentValidationResult Unauthorized() => new(JobAssignmentValidationStatus.Unauthorized);
    public static JobAssignmentValidationResult JobNotFound() => new(JobAssignmentValidationStatus.JobNotFound);
    public static JobAssignmentValidationResult InvalidAssignee() => new(
        JobAssignmentValidationStatus.InvalidAssignee,
        "Sager kan kun tildeles medarbejdere eller administratorer i samme filial.");
}

public interface IJobAssignmentValidator
{
    Task<JobAssignmentValidationResult> ValidateForExistingJobAsync(
        Guid jobId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken);

    Task<JobAssignmentValidationResult> ValidateForDefaultFilialAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken);
}

public sealed class JobAssignmentValidator(
    IJobAssignmentScopeRepository repository,
    ICurrentUserContext currentUser) : IJobAssignmentValidator
{
    public async Task<JobAssignmentValidationResult> ValidateForExistingJobAsync(
        Guid jobId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return JobAssignmentValidationResult.Unauthorized();
        }

        var filialId = await repository.GetJobFilialIdAsync(
            organizationId.Value,
            jobId,
            cancellationToken);
        if (filialId is null)
        {
            return JobAssignmentValidationResult.JobNotFound();
        }

        return await ValidateUsersAsync(
            organizationId.Value,
            filialId.Value,
            userIds,
            cancellationToken);
    }

    public async Task<JobAssignmentValidationResult> ValidateForDefaultFilialAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return JobAssignmentValidationResult.Unauthorized();
        }

        if (userIds.Count == 0)
        {
            return JobAssignmentValidationResult.Valid();
        }

        var filialId = await repository.GetDefaultFilialIdAsync(
            organizationId.Value,
            cancellationToken);
        if (filialId is null)
        {
            throw new InvalidOperationException(
                $"Organization '{organizationId.Value}' has no default filial for job assignment.");
        }

        return await ValidateUsersAsync(
            organizationId.Value,
            filialId.Value,
            userIds,
            cancellationToken);
    }

    private async Task<JobAssignmentValidationResult> ValidateUsersAsync(
        Guid organizationId,
        Guid filialId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var normalizedUserIds = userIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (normalizedUserIds.Length != userIds.Distinct().Count())
        {
            return JobAssignmentValidationResult.InvalidAssignee();
        }

        if (normalizedUserIds.Length == 0)
        {
            return JobAssignmentValidationResult.Valid();
        }

        var users = await repository.GetUserScopesAsync(
            organizationId,
            normalizedUserIds,
            cancellationToken);

        if (users.Count != normalizedUserIds.Length)
        {
            return JobAssignmentValidationResult.InvalidAssignee();
        }

        return users.All(user => JobAssignmentPolicy.CanReceiveAssignmentInFilial(
                user.Role,
                user.FilialId,
                filialId))
            ? JobAssignmentValidationResult.Valid()
            : JobAssignmentValidationResult.InvalidAssignee();
    }
}
