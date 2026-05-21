namespace Workslip.Application.Jobs;

public static class JobRequestValidator
{
    private static readonly HashSet<string> AllowedClosureFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "ikkeFaerdig",
        "faerdig",
        "driftVedligehold",
        "klarTilFaktura"
    };

    public static IReadOnlyList<JobValidationError> ValidateCreate(CreateJobRequest request)
    {
        var errors = new List<JobValidationError>();

        Required(request.ReportNumber, "reportNumber", errors);
        Required(request.CustomerName, "customerName", errors);
        Required(request.CustomerAddress, "customerAddress", errors);
        Required(request.TaskDescription, "taskDescription", errors);

        ValidateInstallationTypes(request.InstallationTypes, errors);
        ValidateWorkKind(request.WorkKind, request.CustomWorkKind, errors);
        ValidateClosureFlags(request.ClosureFlags, errors);
        ValidateControlCategories(request.ControlCategories, errors);

        return errors;
    }

    public static IReadOnlyList<JobValidationError> ValidateUpdate(UpdateJobRequest request)
    {
        var errors = new List<JobValidationError>();

        if (request.InstallationTypes is not null)
        {
            ValidateInstallationTypes(request.InstallationTypes, errors);
        }

        if (request.WorkKind is not null || request.CustomWorkKind is not null)
        {
            ValidateWorkKind(request.WorkKind, request.CustomWorkKind, errors);
        }

        if (request.ClosureFlags is not null)
        {
            ValidateClosureFlags(request.ClosureFlags, errors);
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

    private static void ValidateWorkKind(string? workKind, string? customWorkKind, List<JobValidationError> errors)
    {
        Required(workKind, "workKind", errors);
        if (string.Equals(workKind, "serviceAndet", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(customWorkKind))
        {
            errors.Add(new("customWorkKind", "Custom work kind is required when work kind is Andet."));
        }
    }

    private static void ValidateClosureFlags(IReadOnlyList<string> closureFlags, List<JobValidationError> errors)
    {
        AddDuplicateErrors(closureFlags, "closureFlags", errors);

        foreach (var flag in closureFlags.Where(flag => !AllowedClosureFlags.Contains(flag)))
        {
            errors.Add(new("closureFlags", $"Unknown closure flag '{flag}'."));
        }

        if (closureFlags.Any(flag => string.Equals(flag, "ikkeFaerdig", StringComparison.OrdinalIgnoreCase)) && closureFlags.Count > 1)
        {
            errors.Add(new("closureFlags", "ikkeFaerdig cannot be combined with other closure flags."));
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
