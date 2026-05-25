using FluentValidation;

namespace Workslip.Application.Jobs.Validators;

public static class JobRequestValidationRules
{
    internal static readonly string[] ValidWorkKinds = ["nyInstallation", "aendring", "reparation", "serviceAndet"];
    internal static readonly string[] ExclusiveClosureFlags = ["afvigelse"];

    internal static bool HaveNoDuplicates(IReadOnlyList<string>? items) =>
        items is null || items.Where(i => !string.IsNullOrWhiteSpace(i))
            .GroupBy(i => i.Trim(), StringComparer.OrdinalIgnoreCase)
            .All(g => g.Count() <= 1);

    internal static bool BeKnownWorkKind(string? workKind) =>
        string.IsNullOrWhiteSpace(workKind) || ValidWorkKinds.Contains(workKind, StringComparer.OrdinalIgnoreCase);

    internal static bool NotHaveCustomWorkKindOnFixedWorkKind(string? workKind, string? customWorkKind)
    {
        if (string.IsNullOrWhiteSpace(workKind) || string.IsNullOrWhiteSpace(customWorkKind))
        {
            return true;
        }

        return string.Equals(workKind, "serviceAndet", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool NotContainExclusiveWithOthers(IReadOnlyList<string>? closureFlags)
    {
        if (closureFlags is null)
        {
            return true;
        }

        var hasExclusive = closureFlags.Any(flag =>
            !string.IsNullOrWhiteSpace(flag) &&
            ExclusiveClosureFlags.Contains(flag.Trim(), StringComparer.OrdinalIgnoreCase));

        return !hasExclusive || closureFlags.Count <= 1;
    }
}
