using FluentValidation;

namespace Workslip.Application.Jobs.Validators;

public sealed class CreateJobRequestValidator : AbstractValidator<CreateJobRequest>
{
    public CreateJobRequestValidator()
    {
        RuleFor(x => x.ReportNumber)
            .MaximumLength(80).WithMessage("Report number must not exceed 80 characters.");

        RuleFor(x => x.CustomerName)
            .MaximumLength(240).WithMessage("Customer name must not exceed 240 characters.");

        RuleFor(x => x.CustomerAddress)
            .MaximumLength(500).WithMessage("Customer address must not exceed 500 characters.");

        RuleFor(x => x.CustomerEmail)
            .EmailAddress().WithMessage("Customer email is invalid.")
            .When(x => !string.IsNullOrWhiteSpace(x.CustomerEmail));

        RuleFor(x => x.ContactPerson)
            .MaximumLength(200).WithMessage("Contact person must not exceed 200 characters.");

        RuleFor(x => x.Phone)
            .MaximumLength(80).WithMessage("Phone must not exceed 80 characters.");

        RuleFor(x => x.InstallationTypes)
            .Must(JobRequestValidationRules.HaveNoDuplicates).WithMessage("Duplicate installation type is not allowed.");

        RuleFor(x => x.WorkKind)
            .MaximumLength(80).WithMessage("Work kind must not exceed 80 characters.");

        RuleFor(x => x.CustomWorkKind)
            .MaximumLength(160).WithMessage("Custom work kind must not exceed 160 characters.");

        RuleFor(x => x.ClosureFlags)
            .Must(JobRequestValidationRules.HaveNoDuplicates).WithMessage("Duplicate closure flag is not allowed.");

        When(x => x.ControlInstallationTypes is not null, () =>
        {
            RuleForEach(x => x.ControlInstallationTypes)
                .ChildRules(installationType =>
                {
                    installationType.RuleFor(x => x.InstallationTypeId)
                        .NotEmpty().WithMessage("Installation type ID is required.");

                    installationType.RuleForEach(x => x.Subcategories)
                        .ChildRules(subcategory =>
                        {
                            subcategory.RuleFor(x => x.SubcategoryId)
                                .NotEmpty().WithMessage("Subcategory ID is required.");

                            subcategory.RuleForEach(x => x.ControlChecks)
                                .ChildRules(check =>
                                {
                                    check.RuleFor(x => x.ItemId)
                                        .NotEmpty().WithMessage("Item ID is required.");

                                    check.RuleFor(x => x.Note)
                                        .MaximumLength(500).WithMessage("Note must not exceed 500 characters.");
                                });
                        });
                });
        });
    }
}
