using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Mappers;

public static class JobReportMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    private static readonly IReadOnlyDictionary<string, string> DisplayNames = AuditDisplayNames.Labels;
    private static readonly CultureInfo DanishCulture = CultureInfo.GetCultureInfo("da-DK");

    public static JobReportResponse ToResponse(
        JobReportRow row,
        IReadOnlyList<JobLinkInfoResponse> links,
        IReadOnlyList<AssignedUserResponse> assignedUsers,
        IReadOnlyList<WorksheetUserGroupResponse> worksheetEntries,
        IReadOnlyList<InstallationTypeResponse> installationTypes,
        IReadOnlyList<ClosureFlagResponse> closureFlags,
        decimal? totalHours = null)
    {
        var customerRow = row.CustomerRow;
        var organizationName = row.OrganizationRow?.Name ?? "-";
        var organizationCvr = row.OrganizationRow?.Cvr ?? "-";

        string? customerName = row.CustomerName ?? customerRow?.Name;
        string? customerEmail = row.CustomerEmail ?? customerRow?.Email;
        string? customerPhone = row.CustomerPhone ?? customerRow?.Phone;
        string? customerAddress = row.CustomerAddress ?? customerRow?.Address;
        string? customerContactPerson = row.CustomerContactPerson ?? customerRow?.ContactPerson;

        CustomerInfo? customer = null;
        if (customerRow is not null || customerName is not null)
        {
            customer = new CustomerInfo(
                customerRow?.Id ?? row.CustomerId ?? Guid.Empty,
                customerName,
                customerAddress,
                customerEmail,
                customerContactPerson,
                customerPhone);
        }

        return new(
            row.Id, row.OrganizationId, organizationName, organizationCvr,
            customer,
            row.ReportNumber, ParseStatus(row.Status), ToDateOnly(row.ReportDate),
            row.TaskDescription, row.CustomerObservations, row.TechnicalObservations,
            installationTypes, ToWorkKindResponse(row.WorkKindRow, row.CustomWorkKind),
            row.Remarks, closureFlags, links,
            row.CreatedAt, row.UpdatedAt,
            assignedUsers, worksheetEntries,
            row.IsSoftDeleted, row.DeletionScheduledAt, totalHours);
    }

    public static JobHistoryResponse ToHistoryResponse(JobEventRow row, string? actorName)
    {
        var before = ToJsonObject(row.BeforeJson);
        var after = ToJsonObject(row.AfterJson);
        var changes = new List<PropertyChange>();

        if (before != null || after != null)
        {
            var keys = (before?.Select(x => x.Key) ?? Enumerable.Empty<string>())
                .Union(after?.Select(x => x.Key) ?? Enumerable.Empty<string>())
                .Distinct();

            foreach (var key in keys)
            {
                var rawBeforeValue = before?[key]?.ToString();
                var rawAfterValue = after?[key]?.ToString();
                var beforeValue = FormatHistoryValue(rawBeforeValue);
                var afterValue = FormatHistoryValue(rawAfterValue);
                var displayName = DisplayNames.GetValueOrDefault(key, key);

                if (rawBeforeValue != rawAfterValue)
                {
                    changes.Add(new PropertyChange(
                        key,
                        displayName,
                        beforeValue,
                        afterValue));
                }
            }
        }

        return new JobHistoryResponse(
            row.Id,
            row.ActorId,
            actorName,
            row.EventType,
            BuildDanishHistorySummary(row.EventType, changes),
            changes,
            row.CreatedAt);
    }

    public static JobEventResponse ToEventResponse(JobEventRow row) => new(
        row.Id, row.ReportId ?? Guid.Empty, row.ActorId, row.EventType, row.Summary,
        ToJsonObject(row.BeforeJson), ToJsonObject(row.AfterJson), row.CreatedAt);

    public static async Task<IReadOnlyList<InstallationTypeResponse>> LoadInstallationTypesAsync(
        this SqlDbContext dbContext, Guid organizationId, Guid jobReportId, CancellationToken cancellationToken)
    {
        var installations = await dbContext.JobReportInstallations
            .AsNoTracking()
            .AsSplitQuery()
            .Where(i => i.OrganizationId == organizationId && i.JobReportId == jobReportId)
            .OrderBy(i => i.SortOrder)
            .Select(i => new
            {
                DefId = i.InstallationTypeDefinition.Id,
                DefName = i.InstallationTypeDefinition.Name,
                i.SortOrder,
                Categories = i.Categories
                    .OrderBy(c => c.SortOrder)
                    .Select(c => new
                    {
                        CatId = c.ControlCategory.Id,
                        CatName = c.ControlCategory.Name,
                        c.SortOrder,
                        c.IsIrrelevant,
                        ControlPoints = c.ControlPoints
                            .OrderBy(cp => cp.SortOrder)
                            .Select(cp => new
                            {
                                cp.ControlPoint.Id,
                                cp.ControlPoint.Name,
                                cp.SortOrder,
                                cp.IsRequired,
                                cp.IsChecked
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return installations.Select(i => new InstallationTypeResponse(
            i.DefId, i.DefName, i.SortOrder, i.Categories.Select(c => new InstallationTypeCategoryResponse(
                c.CatId, c.CatName, c.SortOrder,
                c.ControlPoints.Select(cp => new InstallationTypeControlPointResponse(
                    cp.Id, cp.Name, cp.SortOrder, cp.IsRequired, cp.IsChecked
                )).ToArray(),
                c.IsIrrelevant
            )).ToArray()
        )).ToArray();
    }

    public static async Task<IReadOnlyList<ClosureFlagResponse>> LoadClosureFlagsAsync(
        this SqlDbContext dbContext, Guid organizationId, Guid jobReportId, CancellationToken cancellationToken)
    {
        var flags = await dbContext.JobReportClosureFlags
            .AsNoTracking()
            .Where(jrcf => jrcf.JobReportId == jobReportId && jrcf.OrganizationId == organizationId)
            .OrderBy(jrcf => jrcf.SortOrder)
            .Select(jrcf => new ClosureFlagResponse(
                jrcf.ClosureFlag.Id,
                jrcf.ClosureFlag.NormalizedLabel,
                jrcf.ClosureFlag.Label,
                jrcf.ClosureFlag.IsExclusive,
                jrcf.ClosureFlag.SortOrder))
            .ToListAsync(cancellationToken);

        return flags;
    }

    public static JobWorkKindResponse? ToWorkKindResponse(JobWorkKindRow? row, string? customWorkKind) =>
        row is null ? null : new(row.Id, row.NormalizedLabel, row.Label, row.RequiresCustomWorkKind, row.SortOrder, customWorkKind);

    public static JobStatus ParseStatus(string status) => Enum.Parse<JobStatus>(status, ignoreCase: true);

    public static DateOnly? ToDateOnly(DateTime? value) =>
        value is null ? null : DateOnly.FromDateTime(value.Value);

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public static JsonObject ToJsonNode<T>(T value) =>
        JsonSerializer.SerializeToNode(value, JsonOptions)?.AsObject() ?? [];

    private static JsonObject? ToJsonObject(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject;

    private static string BuildDanishHistorySummary(string eventType, IReadOnlyList<PropertyChange> changes)
    {
        var visibleChanges = changes.Where(change => !string.IsNullOrWhiteSpace(change.DisplayName ?? change.PropertyName)).ToArray();
        var worksheetSummary = BuildWorksheetSummary(eventType, visibleChanges);
        if (worksheetSummary is not null)
            return worksheetSummary;

        var assignmentSummary = BuildAssignmentSummary(visibleChanges);
        if (assignmentSummary is not null)
            return assignmentSummary;

        var linkSummary = BuildLinkSummary(visibleChanges);
        if (linkSummary is not null)
            return linkSummary;

        var installationSummary = BuildInstallationSummary(visibleChanges);
        if (installationSummary is not null)
            return installationSummary;

        if (visibleChanges.Length == 0)
        {
            return eventType.ToLowerInvariant() switch
            {
                AuditEventTypes.Added => "Oprettet",
                AuditEventTypes.Deleted => "Slettet",
                AuditEventTypes.Modified => "Ændret",
                _ => "Opdateret"
            };
        }

        if (visibleChanges.Length == 1)
        {
            var change = visibleChanges[0];
            var label = change.DisplayName ?? change.PropertyName;
            return eventType.ToLowerInvariant() switch
            {
                AuditEventTypes.Added => $"{label} tilføjet: '{DisplayValueForSummary(change.After)}'",
                AuditEventTypes.Deleted => $"{label} fjernet: '{DisplayValueForSummary(change.Before)}'",
                AuditEventTypes.Modified => $"{label} ændret: '{DisplayValueForSummary(change.Before)}' → '{DisplayValueForSummary(change.After)}'",
                _ => $"{label} opdateret"
            };
        }

        var fields = string.Join(", ", visibleChanges.Select(change => change.DisplayName ?? change.PropertyName));
        return eventType.ToLowerInvariant() switch
        {
            AuditEventTypes.Added => $"Felter tilføjet: {fields}",
            AuditEventTypes.Deleted => $"Felter fjernet: {fields}",
            AuditEventTypes.Modified => $"Felter ændret: {fields}",
            _ => $"Felter opdateret: {fields}"
        };
    }

    private static string? BuildWorksheetSummary(string eventType, IReadOnlyList<PropertyChange> changes)
    {
        var workDate = FindChange(changes, "WorkDate")?.Before ?? FindChange(changes, "WorkDate")?.After;
        var hours = FindChange(changes, "HoursWorked")?.Before ?? FindChange(changes, "HoursWorked")?.After;
        var hasWorksheetShape = (hours is not null || changes.Count > 1)
            && changes.Any(change => change.PropertyName is "WorkDate" or "HoursWorked" or "SleptOnJob" or AuditFields.AssignedUser or AuditFields.Report);

        if (!hasWorksheetShape)
            return null;

        var datePart = workDate is null ? string.Empty : $" d. {workDate}";
        var hoursPart = hours is null ? string.Empty : $" med {FormatHours(hours)} timer";

        return eventType.ToLowerInvariant() switch
        {
            AuditEventTypes.Added => $"Arbejdsseddel oprettet{datePart}{hoursPart}",
            AuditEventTypes.Deleted => $"Arbejdsseddel fjernet{datePart}{hoursPart}",
            AuditEventTypes.Modified => $"Arbejdsseddel ændret{datePart}: {BuildFieldListSummary(changes) ?? "felter opdateret"}",
            _ => null
        };
    }

    private static string? BuildInstallationSummary(IReadOnlyList<PropertyChange> changes)
    {
        var installationCounts = new Dictionary<string, int>();
        foreach (var change in changes)
        {
            var installationName = GetInstallationName(change.DisplayName ?? change.PropertyName);
            if (installationName is null)
                continue;

            installationCounts[installationName] = installationCounts.GetValueOrDefault(installationName) + 1;
        }

        if (installationCounts.Count == 0)
            return null;

        var summaries = installationCounts.Select(pair =>
            $"{pair.Key}: {pair.Value} {(pair.Value == 1 ? "ændring" : "ændringer")}").ToArray();

        if (summaries.Length <= 3)
            return string.Join(", ", summaries);

        return $"{string.Join(", ", summaries.Take(3))} + {summaries.Length - 3} anlægstyper mere";
    }

    private static string? BuildAssignmentSummary(IReadOnlyList<PropertyChange> changes)
    {
        if (changes.Count <= 1 || changes.Any(change => !IsAssignmentChange(change)))
            return null;

        var added = changes.Count(change => change.Before is null && change.After is not null);
        var removed = changes.Count(change => change.Before is not null && change.After is null);

        return (added, removed) switch
        {
            (> 0, > 0) => $"Tildelte medarbejdere ændret: {added} tilføjet, {removed} fjernet",
            (> 0, 0) => $"Tildelte medarbejdere tilføjet: {added}",
            (0, > 0) => $"Tildelte medarbejdere fjernet: {removed}",
            _ => "Tildelte medarbejdere ændret"
        };
    }

    private static bool IsAssignmentChange(PropertyChange change) =>
        change.PropertyName == AuditFields.AssignedUser
        || change.PropertyName.StartsWith($"{AuditDisplayNames.Labels[AuditFields.AssignedUser]} / ", StringComparison.Ordinal);

    private static string? BuildLinkSummary(IReadOnlyList<PropertyChange> changes)
    {
        if (changes.Count <= 1 || changes.Any(change => !IsLinkChange(change)))
            return null;

        var added = changes.Count(change => change.Before is null && change.After is not null);
        var removed = changes.Count(change => change.Before is not null && change.After is null);

        return (added, removed) switch
        {
            (> 0, > 0) => $"Relaterede sager ændret: {added} tilføjet, {removed} fjernet",
            (> 0, 0) => $"Relaterede sager tilføjet: {added}",
            (0, > 0) => $"Relaterede sager fjernet: {removed}",
            _ => "Relaterede sager ændret"
        };
    }

    private static bool IsLinkChange(PropertyChange change) =>
        change.PropertyName == AuditFields.LinkedReport
        || change.PropertyName.StartsWith($"{AuditDisplayNames.Labels[AuditFields.LinkedReport]} / ", StringComparison.Ordinal);

    private static string? BuildFieldListSummary(IReadOnlyList<PropertyChange> changes)
    {
        var labels = changes
            .Select(change => change.DisplayName ?? change.PropertyName)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToArray();

        if (labels.Length == 0)
            return null;

        if (labels.Length <= 3)
            return string.Join(", ", labels);

        return $"{string.Join(", ", labels.Take(3))} + {labels.Length - 3} mere";
    }

    private static string? GetInstallationName(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var parts = label.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        return parts[0] == AuditDisplayNames.Labels[AuditFields.InstallationType] ? parts[1] : parts[0];
    }

    private static PropertyChange? FindChange(IEnumerable<PropertyChange> changes, string propertyName) =>
        changes.FirstOrDefault(change => change.PropertyName == propertyName);

    private static string FormatHours(string hours) => hours.Replace('.', ',');

    private static string DisplayValueForSummary(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "tom" : value;

    private static string? FormatHistoryValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
            return null;

        if (TryFormatHistoryDate(value, out var formattedDate))
            return formattedDate;

        if (TryFormatJobStatus(value, out var statusLabel))
            return statusLabel;

        if (bool.TryParse(value, out var boolValue))
            return boolValue ? "Ja" : "Nej";

        return Guid.TryParse(value, out _) ? "Ikke vist" : value;
    }

    private static bool TryFormatJobStatus(string value, out string statusLabel)
    {
        statusLabel = value switch
        {
            "Draft" => "Aktiv",
            "InReview" => "Til gennemsyn",
            "Approved" => "Godkendt",
            "Rejected" => "Afvist",
            _ => string.Empty
        };

        return statusLabel.Length > 0;
    }

    private static bool TryFormatHistoryDate(string value, out string formattedDate)
    {
        formattedDate = string.Empty;
        if (!LooksLikeIsoDate(value))
            return false;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dateTimeOffset))
        {
            formattedDate = dateTimeOffset.ToString("d. MMMM yyyy", DanishCulture);
            return true;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dateTime))
        {
            formattedDate = dateTime.ToString("d. MMMM yyyy", DanishCulture);
            return true;
        }

        return false;
    }

    private static bool LooksLikeIsoDate(string value) =>
        value.Length >= 10
        && char.IsDigit(value[0])
        && char.IsDigit(value[1])
        && char.IsDigit(value[2])
        && char.IsDigit(value[3])
        && value[4] == '-'
        && char.IsDigit(value[5])
        && char.IsDigit(value[6])
        && value[7] == '-'
        && char.IsDigit(value[8])
        && char.IsDigit(value[9]);

}

