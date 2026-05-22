namespace Workslip.Application.Jobs;

public static class JobRequestValidator
{
    public static IReadOnlyList<JobValidationError> ValidateCreate(CreateJobRequest request, JobTaxonomySnapshot taxonomy)
    {
        var errors = new List<JobValidationError>();

        Required(request.ReportNumber, "reportNumber", errors);
        Required(request.CustomerName, "customerName", errors);
        Required(request.CustomerAddress, "customerAddress", errors);
        Required(request.TaskDescription, "taskDescription", errors);

        ValidateInstallationTypes(request.InstallationTypes, errors);
        ValidateWorkKind(request.WorkKind, request.CustomWorkKind, taxonomy, errors);
        ValidateClosureFlags(request.ClosureFlags, taxonomy, errors);
        ValidateControlCategories(request.ControlCategories, errors);

        return errors;
    }

    public static IReadOnlyList<JobValidationError> ValidateUpdate(UpdateJobRequest request, JobTaxonomySnapshot taxonomy)
    {
        var errors = new List<JobValidationError>();

        if (request.InstallationTypes is not null)
        {
            ValidateInstallationTypes(request.InstallationTypes, errors);
        }

        if (request.WorkKind is not null || request.CustomWorkKind is not null)
        {
            ValidateWorkKind(request.WorkKind, request.CustomWorkKind, taxonomy, errors);
        }

        if (request.ClosureFlags is not null)
        {
            ValidateClosureFlags(request.ClosureFlags, taxonomy, errors);
        }

        if (request.ControlCategories is not null)
        {
            ValidateControlCategories(request.ControlCategories, errors);
        }

        return errors;
    }

    private static void ValidateInstallationTypes(IReadOnlyList<string> installationTypes, List<JobValidationError> errors)
    {
        if (installationTypes.Count == 0)
        {
            errors.Add(new("installationTypes", "Select at least one installation type."));
            return;
        }

        AddDuplicateErrors(installationTypes, "installationTypes", errors);
    }

    private static void ValidateWorkKind(string? workKind, string? customWorkKind, JobTaxonomySnapshot taxonomy, List<JobValidationError> errors)
    {
        Required(workKind, "workKind", errors);
        if (string.IsNullOrWhiteSpace(workKind))
        {
            return;
        }

        if (!taxonomy.WorkKinds.TryGetValue(workKind, out var definition))
        {
            errors.Add(new("workKind", $"Unknown work kind '{workKind}'."));
            return;
        }

        if (definition.RequiresCustomWorkKind)
        {
            if (string.IsNullOrWhiteSpace(customWorkKind))
            {
                errors.Add(new("customWorkKind", "Custom work kind is required for this work kind."));
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(customWorkKind))
        {
            errors.Add(new("customWorkKind", "Custom work kind is only allowed for work kinds that require custom text."));
        }
    }

    private static void ValidateClosureFlags(IReadOnlyList<string> closureFlags, JobTaxonomySnapshot taxonomy, List<JobValidationError> errors)
    {
        AddDuplicateErrors(closureFlags, "closureFlags", errors);

        var selectedFlags = new List<ClosureFlagDefinition>();
        foreach (var flag in closureFlags.Where(flag => !string.IsNullOrWhiteSpace(flag)))
        {
            if (!taxonomy.ClosureFlags.TryGetValue(flag, out var definition))
            {
                errors.Add(new("closureFlags", $"Unknown closure flag '{flag}'."));
                continue;
            }

            selectedFlags.Add(definition);
        }

        var exclusiveFlag = selectedFlags.FirstOrDefault(flag => flag.IsExclusive);
        if (exclusiveFlag is not null && selectedFlags.Count > 1)
        {
            errors.Add(new("closureFlags", $"{exclusiveFlag.Id} cannot be combined with other closure flags."));
        }
    }

    private static void ValidateControlCategories(IReadOnlyList<ControlCategoryRequest> categories, List<JobValidationError> errors)
    {
        if (categories.Count == 0)
        {
            errors.Add(new("controlCategories", "At least one control category is required."));
            return;
        }

        AddDuplicateErrors(categories.Select(category => category.CategoryId), "controlCategories", errors);

        for (var categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
        {
            var category = categories[categoryIndex];
            var categoryField = $"controlCategories[{categoryIndex}]";
            Required(category.CategoryId, $"{categoryField}.categoryId", errors);

            if (category.Subcategories.Count == 0)
            {
                errors.Add(new($"{categoryField}.subcategories", "At least one subcategory is required."));
                continue;
            }

            AddDuplicateErrors(category.Subcategories.Select(subcategory => subcategory.SubcategoryId), $"{categoryField}.subcategories", errors);

            for (var subcategoryIndex = 0; subcategoryIndex < category.Subcategories.Count; subcategoryIndex++)
            {
                var subcategory = category.Subcategories[subcategoryIndex];
                var subcategoryField = $"{categoryField}.subcategories[{subcategoryIndex}]";
                Required(subcategory.SubcategoryId, $"{subcategoryField}.subcategoryId", errors);
                AddDuplicateErrors(subcategory.ControlChecks.Select(check => check.ItemId), $"{subcategoryField}.controlChecks", errors);

                var checkedItems = subcategory.ControlChecks.Where(check => check.Checked).ToArray();
                if (subcategory.IsIrrelevant)
                {
                    if (checkedItems.Length > 0)
                    {
                        errors.Add(new(subcategoryField, "An irrelevant subcategory cannot also have a selected control item."));
                    }

                    continue;
                }

                if (checkedItems.Length != 1)
                {
                    errors.Add(new(subcategoryField, "Select exactly one control item or mark the subcategory irrelevant."));
                }
            }
        }
    }

    private static void AddDuplicateErrors(IEnumerable<string> values, string field, List<JobValidationError> errors)
    {
        foreach (var duplicate in values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key))
        {
            errors.Add(new(field, $"Duplicate value '{duplicate}' is not allowed."));
        }
    }

    private static void Required(string? value, string field, List<JobValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(field, $"{field} is required."));
        }
    }
}
