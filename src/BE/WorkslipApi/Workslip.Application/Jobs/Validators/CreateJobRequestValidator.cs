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

        When(x => x.Work is not null, () =>
        {
            RuleFor(x => x.Work!.InstallationTypes)
                .Must(JobRequestValidationRules.HaveNoDuplicateInstallations)
                .WithMessage("Duplicate installation type is not allowed.");

            RuleForEach(x => x.Work!.InstallationTypes)
                .ChildRules(installation =>
                {
                    installation.RuleFor(x => x.Id)
                        .NotEmpty().WithMessage("Installation type id is required.");

                    installation.RuleFor(x => x.Categories)
                        .Must(JobRequestValidationRules.HaveNoDuplicateCategories)
                        .WithMessage("Duplicate category is not allowed for an installation type.");

                    installation.RuleForEach(x => x.Categories)
                        .ChildRules(category =>
                        {
                            category.RuleFor(x => x.Id)
                                .NotEmpty().WithMessage("Category id is required.");

                            category.RuleFor(x => x.ControlPoints)
                                .Must(JobRequestValidationRules.HaveNoDuplicateControlPoints)
                                .WithMessage("Duplicate control point is not allowed for a category.");

                            category.RuleForEach(x => x.ControlPoints)
                                .ChildRules(controlPoint =>
                                {
                                    controlPoint.RuleFor(x => x.Id)
                                        .NotEmpty().WithMessage("Control point id is required.");
                                });
                        });
                });

            RuleFor(x => x.Work!.WorkKind)
                .MaximumLength(80).WithMessage("Work kind must not exceed 80 characters.");

            RuleFor(x => x.Work!.CustomWorkKind)
                .MaximumLength(160).WithMessage("Custom work kind must not exceed 160 characters.");

            RuleFor(x => x.Work!.ClosureFlags)
                .Must(JobRequestValidationRules.HaveNoDuplicates).WithMessage("Duplicate closure flag is not allowed.");
        });
    }
}
