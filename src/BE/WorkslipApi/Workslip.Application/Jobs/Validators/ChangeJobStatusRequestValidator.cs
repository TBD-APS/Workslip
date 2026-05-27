using FluentValidation;
using Workslip.Domain;

namespace Workslip.Application.Jobs.Validators;

public sealed class ChangeJobStatusRequestValidator : AbstractValidator<ChangeJobStatusRequest>
{
    private static readonly HashSet<JobStatus> AllowedStatuses =
    [
        JobStatus.Submitted,
        JobStatus.Approved,
        JobStatus.Rejected
    ];

    public ChangeJobStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .Must(status => AllowedStatuses.Contains(status))
            .WithMessage($"Status must be one of: {string.Join(", ", AllowedStatuses.Select(s => s.ToString()))}.");
    }
}
