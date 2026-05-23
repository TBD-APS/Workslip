using FluentValidation;

namespace Workslip.Application.Jobs.Validators;

public class UpdateJobRequestValidator : AbstractValidator<UpdateJobRequest>
{
    public UpdateJobRequestValidator()
    {
        RuleFor(x => x.ReportNumber)
            .When(x => x.ReportNumber != null)
            .NotEmpty().WithMessage("Report number is required.");
            
        RuleFor(x => x.CustomerName)
            .When(x => x.CustomerName != null)
            .NotEmpty().WithMessage("Customer name is required.");
            
        RuleFor(x => x.CustomerAddress)
            .When(x => x.CustomerAddress != null)
            .NotEmpty().WithMessage("Customer address is required.");
            
        RuleFor(x => x.TaskDescription)
            .When(x => x.TaskDescription != null)
            .NotEmpty().WithMessage("Task description is required.");
            
        RuleFor(x => x.InstallationTypes)
            .When(x => x.InstallationTypes != null)
            .NotEmpty().WithMessage("Select at least one installation type.")
            .Must(HaveNoDuplicates).WithMessage("Duplicate installation type is not allowed.");
            
        RuleFor(x => x.WorkKind)
            .When(x => x.WorkKind != null)
            .NotEmpty().WithMessage("Work kind is required.")
            .Must(BeValidWorkKind).WithMessage("Unknown work kind '{PropertyValue}'.")
            .DependentRules(() =>
            {
                RuleFor(x => x.CustomWorkKind)
                    .Must((request, customWorkKind) => 
                        BeValidCustomWorkKindWhenRequired(request.WorkKind, customWorkKind))
                    .WithMessage("Custom work kind is required for this work kind.");
                    
                RuleFor(x => x.CustomWorkKind)
                    .Must((request, customWorkKind) => 
                        BeValidCustomWorkKindWhenNotRequired(request.WorkKind, customWorkKind))
                    .WithMessage("Custom work kind is only allowed for work kinds that require custom text.");
            });
            
        RuleFor(x => x.ClosureFlags)
            .When(x => x.ClosureFlags != null)
            .Must(HaveNoDuplicates).WithMessage("Duplicate closure flag is not allowed.")
            .Must(NotContainExclusiveWithOthers)
            .WithMessage("'{PropertyValue}' cannot be combined with other closure flags.");
            
        RuleForEach(x => x.ControlInstallationTypes)
            .When(x => x.ControlInstallationTypes != null)
            .ChildRules(installationType =>
            {
                installationType.RuleFor(x => x.InstallationTypeId)
                    .NotEmpty().WithMessage("Installation type ID is required.");
                    
                installationType.RuleFor(x => x.Subcategories)
                    .NotEmpty().WithMessage("At least one subcategory is required.")
                    .Must(HaveNoDuplicates).WithMessage("Duplicate subcategory is not allowed.");
                    
                installationType.RuleForEach(x => x.Subcategories)
                    .ChildRules(subcategory =>
                    {
                        subcategory.RuleFor(x => x.SubcategoryId)
                            .NotEmpty().WithMessage("Subcategory ID is required.");
                            
                        subcategory.RuleForEach(x => x.Subcategories)
                            .ChildRules(subSubcategory =>
                            {
                                subSubcategory.RuleFor(x => x.ControlChecks)
                                    .NotEmpty().WithMessage("At least one control check is required.");
                                    
                                subSubcategory.RuleForEach(x => x.ControlChecks)
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

    private bool HaveNoDuplicates(IReadOnlyList<string> items)
    {
        if (items == null) return true;
        return items.Where(i => !string.IsNullOrWhiteSpace(i))
                   .GroupBy(i => i, StringComparer.OrdinalIgnoreCase)
                   .All(g => g.Count() <= 1);
    }

    private bool BeValidWorkKind(string workKind)
    {
        if (string.IsNullOrWhiteSpace(workKind))
            return false;
            
        var validWorkKinds = new[] { "nyInstallation", "aendring", "reparation", "serviceAndet" };
        return validWorkKinds.Contains(workKind, StringComparer.OrdinalIgnoreCase);
    }

    private bool BeValidCustomWorkKindWhenRequired(string workKind, string? customWorkKind)
    {
        if (string.IsNullOrWhiteSpace(workKind))
            return true;
            
        var workKindLower = workKind.ToLower();
        return !(workKindLower == "serviceandet" && string.IsNullOrWhiteSpace(customWorkKind));
    }

    private bool BeValidCustomWorkKindWhenNotRequired(string workKind, string? customWorkKind)
    {
        if (string.IsNullOrWhiteSpace(workKind) || string.IsNullOrWhiteSpace(customWorkKind))
            return true;
            
        var workKindLower = workKind.ToLower();
        return !(workKindLower == "serviceandet" && !string.IsNullOrWhiteSpace(customWorkKind));
    }

    private bool NotContainExclusiveWithOthers(IReadOnlyList<string> closureFlags)
    {
        if (closureFlags == null) return true;
        
        var exclusiveFlags = new[] { "afvigelse" };
        
        var hasExclusive = closureFlags.Any(flag => 
            !string.IsNullOrWhiteSpace(flag) && 
            exclusiveFlags.Contains(flag.Trim(), StringComparer.OrdinalIgnoreCase));
            
        return !hasExclusive || closureFlags.Count <= 1;
    }

    private bool NotHaveIrrelevantWithOthers(IReadOnlyList<ControlCheckRequest> checks)
    {
        if (checks == null) return true;
        
        var checkedItems = checks.Where(c => c.Checked).ToArray();
        var checkedIrrelevantItems = checkedItems.Where(c => c.ItemId.EndsWith("-irrelevant", StringComparison.OrdinalIgnoreCase)).ToArray();
        
        return !(checkedIrrelevantItems.Length > 0 && checkedItems.Length > checkedIrrelevantItems.Length);
    }
}
