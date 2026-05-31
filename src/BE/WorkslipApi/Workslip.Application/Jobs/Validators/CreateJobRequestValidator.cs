using FluentValidation;

namespace Workslip.Application.Jobs.Validators;

public sealed class CreateJobRequestValidator : AbstractValidator<CreateJobRequest>
{
    public CreateJobRequestValidator()
    {
        RuleFor(x => x.ReportNumber)
            .MaximumLength(80).WithMessage("Report number must not exceed 80 characters.");

        RuleFor(x => x.Customer)
            .SetValidator(new CustomerInfoValidator()!)
            .When(x => x.Customer is not null);

        RuleFor(x => x.InstallationTypes)
            .Must(JobRequestValidationRules.HaveNoDuplicates).WithMessage("Duplicate installation type is not allowed.");

        RuleFor(x => x.WorkKind)
            .MaximumLength(80).WithMessage("Work kind must not exceed 80 characters.");

        RuleFor(x => x.CustomWorkKind)
            .MaximumLength(160).WithMessage("Custom work kind must not exceed 160 characters.");

        RuleFor(x => x.ClosureFlags)
            .Must(JobRequestValidationRules.HaveNoDuplicates).WithMessage("Duplicate closure flag is not allowed.");

    }
}
