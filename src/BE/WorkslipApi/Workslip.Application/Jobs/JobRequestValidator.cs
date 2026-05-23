using Workslip.Domain.Models;

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
        ValidateControlInstallationTypes(request.ControlInstallationTypes, request.InstallationTypes, "controlInstallationTypes", errors);

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

        if (request.ControlInstallationTypes is not null)
        {
            ValidateControlInstallationTypes(request.ControlInstallationTypes, request.InstallationTypes, "controlInstallationTypes", errors);
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

    private static void ValidateControlInstallationTypes(
        IReadOnlyList<ControlInstallationTypeRequest> installationTypes,
        IReadOnlyList<string>? selectedInstallationTypes,
        string field,
        List<JobValidationError> errors)
    {
        if (installationTypes.Count == 0)
        {
            errors.Add(new(field, "At least one control installation type is required."));
            return;
        }

        AddDuplicateErrors(installationTypes.Select(installationType => installationType.InstallationTypeId), field, errors);
        var selected = selectedInstallationTypes?.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var installationTypeIndex = 0; installationTypeIndex < installationTypes.Count; installationTypeIndex++)
        {
            var installationType = installationTypes[installationTypeIndex];
            var installationTypeField = $"{field}[{installationTypeIndex}]";
            Required(installationType.InstallationTypeId, $"{installationTypeField}.installationTypeId", errors);

            if (selected is not null && !string.IsNullOrWhiteSpace(installationType.InstallationTypeId) && !selected.Contains(installationType.InstallationTypeId))
            {
                errors.Add(new($"{installationTypeField}.installationTypeId", "Control installation type must be selected on the job."));
            }

            if (installationType.Subcategories.Count == 0)
            {
                errors.Add(new($"{installationTypeField}.subcategories", "At least one subcategory is required."));
                continue;
            }

            AddDuplicateErrors(installationType.Subcategories.Select(subcategory => subcategory.SubcategoryId), $"{installationTypeField}.subcategories", errors);

            for (var subcategoryIndex = 0; subcategoryIndex < installationType.Subcategories.Count; subcategoryIndex++)
            {
                var subcategory = installationType.Subcategories[subcategoryIndex];
                var subcategoryField = $"{installationTypeField}.subcategories[{subcategoryIndex}]";
                Required(subcategory.SubcategoryId, $"{subcategoryField}.subcategoryId", errors);
                AddDuplicateErrors(subcategory.ControlChecks.Select(check => check.ItemId), $"{subcategoryField}.controlChecks", errors);

                var checkedItems = subcategory.ControlChecks.Where(check => check.Checked).ToArray();
                var checkedIrrelevantItems = checkedItems.Where(check => IsIrrelevantItem(check.ItemId)).ToArray();
                if (checkedIrrelevantItems.Length > 0 && checkedItems.Length > checkedIrrelevantItems.Length)
                {
                    errors.Add(new(subcategoryField, "Irrelevant cannot be combined with other selected control items."));
                }
            }
        }
    }

    private static bool IsIrrelevantItem(string itemId) =>
        itemId.EndsWith("-irrelevant", StringComparison.OrdinalIgnoreCase);

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
