using FluentValidation;
using Workslip.Application.Jobs;
using Workslip.Domain.Models;

namespace Workslip.Application.Jobs.Validators;

public class UpdateJobRequestValidator : AbstractValidator<UpdateJobRequest>
{
    public UpdateJobRequestValidator()
    {
        RuleFor(x => x.ReportNumber)
            .NotEmpty().WithMessage("Report number is required.")
            .When(x => x.ReportNumber != null);

        RuleFor(x => x.CustomerName)
            .NotEmpty().WithMessage("Customer name is required.")
            .When(x => x.CustomerName != null);

        RuleFor(x => x.CustomerAddress)
            .NotEmpty().WithMessage("Customer address is required.")
            .When(x => x.CustomerAddress != null);

        RuleFor(x => x.TaskDescription)
            .NotEmpty().WithMessage("Task description is required.")
            .When(x => x.TaskDescription != null);

        RuleFor(x => x.InstallationTypes)
            .NotEmpty().WithMessage("Select at least one installation type.")
            .Must(HaveNoDuplicates).WithMessage("Duplicate installation type is not allowed.")
            .When(x => x.InstallationTypes != null);

        RuleFor(x => x.WorkKind)
            .NotEmpty().WithMessage("Work kind is required.")
            .Must(BeValidWorkKind).WithMessage("Unknown work kind '{PropertyValue}'.")
            .When(x => x.WorkKind != null)
            .DependentRules(() =>
            {
                RuleFor(x => x.CustomWorkKind)
                    .Must((request, customWorkKind) => BeValidCustomWorkKindWhenRequired(request.WorkKind, customWorkKind))
                    .WithMessage("Custom work kind is required for this work kind.");

                RuleFor(x => x.CustomWorkKind)
                    .Must((request, customWorkKind) => BeValidCustomWorkKindWhenNotRequired(request.WorkKind, customWorkKind))
                    .WithMessage("Custom work kind is only allowed for work kinds that require custom text.");
            });

        RuleFor(x => x.ClosureFlags)
            .Must(HaveNoDuplicates).WithMessage("Duplicate closure flag is not allowed.")
            .Must(NotContainExclusiveWithOthers)
            .WithMessage("'{PropertyValue}' cannot be combined with other closure flags.")
            .When(x => x.ClosureFlags != null);

        When(x => x.ControlInstallationTypes != null, () =>
        {
            RuleForEach(x => x.ControlInstallationTypes)
                .ChildRules(installationType =>
                {
                    installationType.RuleFor(x => x.InstallationTypeId)
                        .NotEmpty().WithMessage("Installation type ID is required.");

                    installationType.RuleFor(x => x.Subcategories)
                        .NotEmpty().WithMessage("At least one subcategory is required.");

                    installationType.RuleForEach(x => x.Subcategories)
                        .ChildRules(subcategory =>
                        {
                            subcategory.RuleFor(x => x.SubcategoryId)
                                .NotEmpty().WithMessage("Subcategory ID is required.");

                            subcategory.RuleFor(x => x.ControlChecks)
                                .NotEmpty().WithMessage("At least one control check is required.");

                            subcategory.RuleForEach(x => x.ControlChecks)
                                .ChildRules(check =>
                                {
                                    check.RuleFor(x => x.ItemId)
                                        .NotEmpty().WithMessage("Item ID is required.");

                                    check.RuleFor(x => x.Checked)
                                        .NotNull().WithMessage("Checked value is required.");

                                    check.RuleFor(x => x.Note)
                                        .MaximumLength(500).WithMessage("Note must not exceed 500 characters.");
                                });
                        });
                });
        });
    }

    private bool HaveNoDuplicates(IReadOnlyList<string>? items)
    {
        if (items == null) return true;
        return items.Where(i => !string.IsNullOrWhiteSpace(i))
                   .GroupBy(i => i, StringComparer.OrdinalIgnoreCase)
                   .All(g => g.Count() <= 1);
    }

    private bool BeValidWorkKind(string? workKind)
    {
        if (string.IsNullOrWhiteSpace(workKind))
            return false;

        var validWorkKinds = new[] { "nyInstallation", "aendring", "reparation", "serviceAndet" };
        return validWorkKinds.Contains(workKind!, StringComparer.OrdinalIgnoreCase);
    }

    private bool BeValidCustomWorkKindWhenRequired(string? workKind, string? customWorkKind)
    {
        if (string.IsNullOrWhiteSpace(workKind))
            return true;

        var workKindLower = workKind!.ToLower();
        return !(workKindLower == "serviceandet" && string.IsNullOrWhiteSpace(customWorkKind));
    }

    private bool BeValidCustomWorkKindWhenNotRequired(string? workKind, string? customWorkKind)
    {
        if (string.IsNullOrWhiteSpace(workKind) || string.IsNullOrWhiteSpace(customWorkKind))
            return true;

        var workKindLower = workKind!.ToLower();
        return !(workKindLower != "serviceandet" && !string.IsNullOrWhiteSpace(customWorkKind));
    }

    private bool NotContainExclusiveWithOthers(IReadOnlyList<string>? closureFlags)
    {
        if (closureFlags == null) return true;

        var exclusiveFlags = new[] { "afvigelse" };

        var hasExclusive = closureFlags.Any(flag =>
            !string.IsNullOrWhiteSpace(flag) &&
            exclusiveFlags.Contains(flag.Trim(), StringComparer.OrdinalIgnoreCase));

        return !hasExclusive || closureFlags.Count <= 1;
    }
}
