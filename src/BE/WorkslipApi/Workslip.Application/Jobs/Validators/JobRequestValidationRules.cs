namespace Workslip.Application.Jobs.Validators;

public static class JobRequestValidationRules
{
    internal static bool HaveNoDuplicates(IReadOnlyList<string>? items) =>
        items is null || items.Where(i => !string.IsNullOrWhiteSpace(i))
            .GroupBy(i => i.Trim(), StringComparer.OrdinalIgnoreCase)
            .All(g => g.Count() <= 1);

    public static bool HaveNoDuplicateInstallations(IReadOnlyList<CreateInstallationTypeRequest>? items) =>
        items is null || items.Where(i => i is not null && i.Id != Guid.Empty)
            .GroupBy(i => i!.Id)
            .All(g => g.Count() <= 1);

    public static bool HaveNoDuplicateCategories(IReadOnlyList<CreateInstallationTypeCategoryRequest>? items) =>
        items is null || items.Where(i => i is not null && i.Id != Guid.Empty)
            .GroupBy(i => i!.Id)
            .All(g => g.Count() <= 1);

    public static bool HaveNoDuplicateControlPoints(IReadOnlyList<CreateInstallationTypeControlPointRequest>? items) =>
        items is null || items.Where(i => i is not null && i.Id != Guid.Empty)
            .GroupBy(i => i!.Id)
            .All(g => g.Count() <= 1);
}
