using Workslip.Domain;

namespace Workslip.Application.Jobs;

public static class AuditorDataScope
{
    public const string WaterInstallationType = "Vand";
    public const string DrainageInstallationType = "Afløb";

    private const string InstallationTypeHistoryProperty = "InstallationType";
    private const string InstallationCategoryHistoryProperty = "InstallationCategory";
    private const string ControlPointHistoryProperty = "ControlPoint";
    private const string LinkedReportHistoryProperty = "LinkedReport";
    private const string IsCheckedHistoryProperty = "IsChecked";
    private const string IsIrrelevantHistoryProperty = "IsIrrelevant";

    private static readonly HashSet<string> AllowedInstallationTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        WaterInstallationType,
        DrainageInstallationType
    };

    public static bool AppliesTo(string? role) =>
        string.Equals(role, Roles.Auditor, StringComparison.OrdinalIgnoreCase);

    public static bool IsInstallationTypeVisible(string? installationTypeName) =>
        !string.IsNullOrWhiteSpace(installationTypeName)
        && AllowedInstallationTypeNames.Contains(installationTypeName.Trim());

    public static bool CanAccess(JobReportResponse report) =>
        report.InstallationTypes.Any(type => IsInstallationTypeVisible(type.Name));

    public static ReferenceDataResponse Filter(ReferenceDataResponse data) =>
        data with
        {
            InstallationTypes = data.InstallationTypes
                .Where(type => IsInstallationTypeVisible(type.Name))
                .ToArray()
        };

    public static JobListItemResponse? Filter(JobListItemResponse item)
    {
        var visibleInstallationTypes = item.InstallationTypes
            .Where(IsInstallationTypeVisible)
            .ToArray();

        return visibleInstallationTypes.Length == 0
            ? null
            : item with { InstallationTypes = visibleInstallationTypes };
    }

    public static JobReportSummaryResponse? Filter(JobReportSummaryResponse report)
    {
        var visibleInstallationTypes = report.Work.InstallationTypes
            .Where(type => IsInstallationTypeVisible(type.Name))
            .ToArray();

        if (visibleInstallationTypes.Length == 0)
        {
            return null;
        }

        var visibleInstallationTypeIds = visibleInstallationTypes
            .Select(type => type.Id.ToString("D"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return report with
        {
            Work = report.Work with { InstallationTypes = visibleInstallationTypes },
            ControlInstallationTypes = report.ControlInstallationTypes
                .Where(control => visibleInstallationTypeIds.Contains(control.InstallationTypeId))
                .ToArray()
        };
    }

    public static IReadOnlyList<JobHistoryResponse> Filter(IReadOnlyList<JobHistoryResponse> events) =>
        events.Where(IsHistoryEventVisible).ToArray();

    private static bool IsHistoryEventVisible(JobHistoryResponse historyEvent)
    {
        var installationTypeChange = historyEvent.Changes
            .FirstOrDefault(change => change.PropertyName == InstallationTypeHistoryProperty);

        if (installationTypeChange is not null)
        {
            var installationType = installationTypeChange.After ?? installationTypeChange.Before;
            return IsInstallationTypeVisible(installationType);
        }

        // Some consolidated audit events intentionally omit an unchanged
        // InstallationType value. In that case discipline-specific category /
        // control-point data cannot be attributed safely, so default-deny it.
        // Historical link metadata is also omitted because the linked report may
        // no longer be within the Auditor's current discipline scope.
        return historyEvent.Changes.All(change => !IsPotentiallyScopedHistoryChange(change));
    }

    private static bool IsPotentiallyScopedHistoryChange(PropertyChange change) =>
        change.PropertyName is InstallationCategoryHistoryProperty
            or ControlPointHistoryProperty
            or LinkedReportHistoryProperty
            or IsCheckedHistoryProperty
            or IsIrrelevantHistoryProperty
        || change.PropertyName.Contains(" / ", StringComparison.Ordinal)
        || change.PropertyName.Contains("(irrelevant)", StringComparison.OrdinalIgnoreCase);
}
