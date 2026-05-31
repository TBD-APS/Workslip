namespace Workslip.Application.Jobs.Validators;

public static class JobRequestValidationRules
{
    internal static bool HaveNoDuplicates(IReadOnlyList<string>? items) =>
        items is null || items.Where(i => !string.IsNullOrWhiteSpace(i))
            .GroupBy(i => i.Trim(), StringComparer.OrdinalIgnoreCase)
            .All(g => g.Count() <= 1);

    internal static bool HaveNoDuplicates(IReadOnlyList<CreateInstallationTypeRequest>? items) =>
        items is null || items.Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .GroupBy(i => i.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .All(g => g.Count() <= 1);
}
