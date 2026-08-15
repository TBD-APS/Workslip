using Ardalis.Result;
using FluentValidation;
using Workslip.Application.Auth;

namespace Workslip.Application.Jobs;

public sealed record JobAuditorScopeResponse(bool IsInAuditorScope, string? Reason);

public sealed record SetJobAuditorScopeRequest(bool IsInAuditorScope, string? Reason);

public interface IJobAuditorScopeRepository
{
    Task<JobAuditorScopeResponse?> GetAsync(
        Guid jobId,
        Guid organizationId,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<Guid>> GetVisibleJobIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken);

    Task<JobAuditorScopeResponse?> SetAsync(
        Guid jobId,
        Guid organizationId,
        bool isInAuditorScope,
        string? reason,
        CancellationToken cancellationToken);
}

public interface IJobAuditorScopeService
{
    Task<Result<JobAuditorScopeResponse>> GetAsync(Guid jobId, CancellationToken cancellationToken);
    Task<Result<JobAuditorScopeResponse>> SetAsync(
        Guid jobId,
        SetJobAuditorScopeRequest request,
        CancellationToken cancellationToken);
}

public sealed class SetJobAuditorScopeRequestValidator : AbstractValidator<SetJobAuditorScopeRequest>
{
    public const int MaxReasonLength = 500;

    public SetJobAuditorScopeRequestValidator()
    {
        RuleFor(request => request.Reason)
            .MaximumLength(MaxReasonLength)
            .WithMessage($"Begrundelsen må højst være {MaxReasonLength} tegn.");

        When(request => !request.IsInAuditorScope, () =>
        {
            RuleFor(request => request.Reason)
                .NotEmpty()
                .WithMessage("Angiv hvorfor sagen ikke skal indgå i auditørens arbejdsflade.")
                .Must(reason => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length >= 3)
                .WithMessage("Begrundelsen skal være på mindst 3 tegn.");
        });
    }
}

public sealed class JobAuditorScopeService(
    IJobAuditorScopeRepository repository,
    IJobService jobService,
    ICurrentUserContext currentUser,
    IValidator<SetJobAuditorScopeRequest> validator) : IJobAuditorScopeService
{
    public async Task<Result<JobAuditorScopeResponse>> GetAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
            return Result<JobAuditorScopeResponse>.Unauthorized();

        var state = await repository.GetAsync(jobId, organizationId.Value, cancellationToken);
        return state is null
            ? Result<JobAuditorScopeResponse>.NotFound()
            : Result<JobAuditorScopeResponse>.Success(state);
    }

    public async Task<Result<JobAuditorScopeResponse>> SetAsync(
        Guid jobId,
        SetJobAuditorScopeRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
            return Result<JobAuditorScopeResponse>.Unauthorized();

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<JobAuditorScopeResponse>.Invalid(
                validation.Errors.Select(error => new ValidationError
                {
                    Identifier = error.PropertyName,
                    ErrorMessage = error.ErrorMessage
                }));
        }

        var normalizedReason = request.IsInAuditorScope
            ? null
            : request.Reason!.Trim();

        var state = await repository.SetAsync(
            jobId,
            organizationId.Value,
            request.IsInAuditorScope,
            normalizedReason,
            cancellationToken);
        if (state is null)
            return Result<JobAuditorScopeResponse>.NotFound();

        await jobService.InvalidateJobDetailCacheAsync(jobId, organizationId.Value, cancellationToken);
        return Result<JobAuditorScopeResponse>.Success(state);
    }
}
