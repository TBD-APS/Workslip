using Workslip.Domain;

namespace Workslip.Application.Jobs;

public static class AuditorDataScope
{
    public const string WaterInstallationType = "Vand";
    public const string DrainageInstallationType = "Afløb";

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
}
