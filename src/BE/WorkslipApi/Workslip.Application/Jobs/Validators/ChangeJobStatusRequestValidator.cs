using FluentValidation;
using Workslip.Domain;

namespace Workslip.Application.Jobs.Validators;

public sealed class ChangeJobStatusRequestValidator : AbstractValidator<ChangeJobStatusRequest>
{
    private static readonly HashSet<JobStatus> AllowedStatuses =
    [
        JobStatus.Draft,
        JobStatus.InReview,
        JobStatus.Approved,
        JobStatus.Rejected
    ];

    public ChangeJobStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .Must(status => AllowedStatuses.Contains(status))
            .WithMessage($"Status skal være en af følgende: {string.Join(", ", AllowedStatuses.Select(s => s.ToString()))}.");

        RuleFor(x => x.RejectionNote)
            .NotEmpty()
            .When(x => x.Status == JobStatus.Rejected)
            .WithMessage("Begrundelse er påkrævet ved afvisning.");
    }
}
