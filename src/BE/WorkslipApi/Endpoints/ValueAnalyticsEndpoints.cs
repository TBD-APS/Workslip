using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Workslip.Application.Analytics;
using Workslip.Domain;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Endpoints;

public static class ValueAnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapValueAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapSuperAdminGroup("/api/superadmin/analytics", "superadmin-analytics");
        group.MapGet("/value-model", GetValueModelAsync)
            .Produces<ValueAnalyticsModelResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> GetValueModelAsync(
        int? days,
        string? scope,
        IConfiguration configuration,
        IHostEnvironment environment,
        SqlDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var windowDays = Math.Clamp(days ?? 365, 30, 730);
        var generatedAt = DateTimeOffset.UtcNow;
        var windowStart = generatedAt.AddDays(-windowDays);

        var selectedScope = string.IsNullOrWhiteSpace(scope)
            ? "customer"
            : scope.Trim().ToLowerInvariant();
        if (selectedScope is not ("customer" or "demo"))
        {
            return Results.BadRequest(new
            {
                error = "invalid_value_analytics_scope",
                allowedScopes = new[] { "customer", "demo" }
            });
        }

        var demoOrganizationIds = new HashSet<Guid>();
        foreach (var configuredId in configuration.GetSection("Analytics:DemoOrganizationIds").Get<string[]>() ?? [])
        {
            if (Guid.TryParse(configuredId, out var parsed))
                demoOrganizationIds.Add(parsed);
        }

        if (selectedScope == "demo" && demoOrganizationIds.Count == 0)
        {
            return Results.Conflict(new
            {
                error = "demo_organization_scope_not_configured",
                configurationKey = "Analytics:DemoOrganizationIds"
            });
        }

        var demoIds = demoOrganizationIds.ToArray();
        var organizationsQuery = dbContext.Organizations
            .AsNoTracking()
            .Where(org => org.Id != PlatformOrganization.Id);

        organizationsQuery = selectedScope == "demo"
            ? organizationsQuery.Where(org => demoIds.Contains(org.Id))
            : organizationsQuery.Where(org => !demoIds.Contains(org.Id));

        var organizations = await organizationsQuery
            .OrderBy(org => org.Name)
            .Select(org => new ValueOrganizationProjection(org.Id, org.Name, org.CreatedAt))
            .ToListAsync(cancellationToken);

        var organizationIds = organizations.Select(org => org.Id).ToArray();

        var employees = organizationIds.Length == 0
            ? []
            : await dbContext.Users
                .AsNoTracking()
                .Where(user =>
                    organizationIds.Contains(user.OrganizationId)
                    && user.Role != Roles.Superadmin)
                .OrderBy(user => user.DisplayName)
                .Select(user => new ValueEmployeeRow(
                    user.Id.ToString(),
                    user.OrganizationId.ToString(),
                    user.DisplayName,
                    user.Role))
                .ToListAsync(cancellationToken);

        var allJobs = organizationIds.Length == 0
            ? []
            : await dbContext.JobReports
                .AsNoTracking()
                .Where(job => organizationIds.Contains(job.OrganizationId) && !job.IsSoftDeleted)
                .Select(job => new ValueJobProjection(
                    job.Id,
                    job.OrganizationId,
                    job.SubmittedByUserId,
                    job.Status,
                    job.CreatedAt,
                    job.UpdatedAt,
                    job.SubmittedAt))
                .ToListAsync(cancellationToken);

        var jobIds = allJobs.Select(job => job.Id).ToArray();

        var lifecycleRows = jobIds.Length == 0
            ? []
            : await dbContext.JobEvents
                .AsNoTracking()
                .Where(evt =>
                    organizationIds.Contains(evt.OrganizationId)
                    && evt.ReportId != null
                    && jobIds.Contains(evt.ReportId.Value)
                    && evt.AfterJson != null
                    && (evt.AfterJson.Contains("Approved")
                        || evt.AfterJson.Contains("Rejected")
                        || evt.AfterJson.Contains("Reopened")))
                .Select(evt => new ValueLifecycleProjection(
                    evt.ReportId!.Value,
                    evt.BeforeJson,
                    evt.AfterJson,
                    evt.CreatedAt))
                .ToListAsync(cancellationToken);

        var lifecycleByJob = lifecycleRows
            .Select(evt => new
            {
                evt.JobId,
                Change = new JobStatusChangeObservation(
                    ReadStatus(evt.BeforeJson),
                    ReadStatus(evt.AfterJson),
                    evt.CreatedAt)
            })
            .Where(item => item.Change.AfterStatus is not null)
            .GroupBy(item => item.JobId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<JobStatusChangeObservation>)group
                    .Select(item => item.Change)
                    .OrderBy(change => change.OccurredAt)
                    .ToArray());

        var completionAtByJob = allJobs.ToDictionary(
            job => job.Id,
            job => ActivationMetricSemantics.ResolveFirstCompletedCompliantAt(
                job.Status,
                job.UpdatedAt,
                lifecycleByJob.GetValueOrDefault(job.Id) ?? []));

        var includedJobs = allJobs
            .Where(job =>
                job.CreatedAt >= windowStart
                || (completionAtByJob[job.Id] is DateTimeOffset completedAt && completedAt >= windowStart))
            .ToList();
        var includedJobIds = includedJobs.Select(job => job.Id).ToArray();

        var categories = includedJobIds.Length == 0
            ? []
            : await (
                from category in dbContext.JobReportInstallationCategories.AsNoTracking()
                join installation in dbContext.JobReportInstallations.AsNoTracking()
                    on new { category.OrganizationId, Id = category.JobReportInstallationId }
                    equals new { installation.OrganizationId, installation.Id }
                where organizationIds.Contains(category.OrganizationId)
                    && includedJobIds.Contains(installation.JobReportId)
                select new ValueCategoryProjection(
                    installation.JobReportId,
                    category.Id,
                    category.IsIrrelevant))
                .ToListAsync(cancellationToken);

        var controlPoints = includedJobIds.Length == 0
            ? []
            : await (
                from controlPoint in dbContext.JobReportInstallationControlPoints.AsNoTracking()
                join category in dbContext.JobReportInstallationCategories.AsNoTracking()
                    on new { controlPoint.OrganizationId, Id = controlPoint.JobReportInstallationCategoryId }
                    equals new { category.OrganizationId, category.Id }
                join installation in dbContext.JobReportInstallations.AsNoTracking()
                    on new { category.OrganizationId, Id = category.JobReportInstallationId }
                    equals new { installation.OrganizationId, installation.Id }
                where organizationIds.Contains(controlPoint.OrganizationId)
                    && includedJobIds.Contains(installation.JobReportId)
                select new ValueControlPointProjection(
                    installation.JobReportId,
                    category.Id,
                    controlPoint.ControlPointId,
                    category.IsIrrelevant,
                    controlPoint.IsRequired,
                    controlPoint.IsChecked))
                .ToListAsync(cancellationToken);

        var categoriesByJob = categories
            .GroupBy(category => category.JobId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var controlPointsByJob = controlPoints
            .GroupBy(controlPoint => controlPoint.JobId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var jobRows = new List<ValueJobRow>();
        var controlPointRows = new List<ValueControlPointResultRow>();

        foreach (var job in includedJobs)
        {
            var jobCategories = categoriesByJob.GetValueOrDefault(job.Id) ?? [];
            var jobControlPoints = controlPointsByJob.GetValueOrDefault(job.Id) ?? [];
            var completedAt = completionAtByJob[job.Id];
            var statusChanges = lifecycleByJob.GetValueOrDefault(job.Id) ?? [];

            var rejectionCount = statusChanges.Count(change =>
                (string.Equals(change.AfterStatus, JobStatus.Rejected.ToString(), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(change.AfterStatus, JobStatus.Reopened.ToString(), StringComparison.OrdinalIgnoreCase))
                && !string.Equals(change.BeforeStatus, change.AfterStatus, StringComparison.OrdinalIgnoreCase));

            var missingDocumentationCount = jobCategories.Count(category =>
                !category.IsIrrelevant
                && !jobControlPoints.Any(controlPoint =>
                    controlPoint.CategoryId == category.CategoryId && controlPoint.IsChecked));

            var requiredControlPoints = jobControlPoints.Count(controlPoint =>
                !controlPoint.IsIrrelevant && controlPoint.IsRequired);
            var completedControlPoints = jobControlPoints.Count(controlPoint =>
                !controlPoint.IsIrrelevant && controlPoint.IsChecked);

            jobRows.Add(new ValueJobRow(
                job.Id.ToString(),
                job.OrganizationId.ToString(),
                job.SubmittedByUserId?.ToString(),
                completedAt.HasValue ? DateKey(completedAt.Value) : null,
                job.Status,
                completedAt.HasValue,
                requiredControlPoints,
                completedControlPoints,
                missingDocumentationCount,
                null,
                rejectionCount,
                rejectionCount > 0,
                completedAt.HasValue && completedAt.Value >= job.CreatedAt
                    ? (long)Math.Round((completedAt.Value - job.CreatedAt).TotalMinutes)
                    : null));

            foreach (var controlPoint in jobControlPoints)
            {
                var result = controlPoint.IsIrrelevant
                    ? "Irrelevant"
                    : controlPoint.IsChecked ? "Checked" : "Missing";

                controlPointRows.Add(new ValueControlPointResultRow(
                    $"{job.Id:N}:{controlPoint.CategoryId:N}:{controlPoint.ControlPointId:N}",
                    job.Id.ToString(),
                    job.OrganizationId.ToString(),
                    completedAt.HasValue ? DateKey(completedAt.Value) : DateKey(job.CreatedAt),
                    result,
                    null,
                    rejectionCount > 0));
            }
        }

        var organizationRows = organizations.Select(org => new ValueOrganizationRow(
            org.Id.ToString(),
            org.Name,
            DateKey(org.CreatedAt))).ToArray();

        return Results.Ok(new ValueAnalyticsModelResponse(
            1,
            ActivationMetricSemantics.DefinitionVersion,
            generatedAt,
            environment.EnvironmentName,
            selectedScope,
            windowDays,
            organizationRows,
            employees,
            jobRows,
            controlPointRows,
            new ValueSourceCoverage(
                "Workslip domain + audit history",
                "External billing source required",
                "External sales/CRM source required",
                "Monthly design-partner survey source required",
                "Lead-source dimension is maintained in the Power BI project"))));
    }

    private static int DateKey(DateTimeOffset value) =>
        value.UtcDateTime.Year * 10_000 + value.UtcDateTime.Month * 100 + value.UtcDateTime.Day;

    private static string? ReadStatus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "status", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Historic audit rows may contain malformed JSON. Ignore only that event.
        }

        return null;
    }

    private sealed record ValueOrganizationProjection(Guid Id, string Name, DateTimeOffset CreatedAt);
    private sealed record ValueJobProjection(
        Guid Id,
        Guid OrganizationId,
        Guid? SubmittedByUserId,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? SubmittedAt);
    private sealed record ValueLifecycleProjection(
        Guid JobId,
        string? BeforeJson,
        string? AfterJson,
        DateTimeOffset CreatedAt);
    private sealed record ValueCategoryProjection(Guid JobId, Guid CategoryId, bool IsIrrelevant);
    private sealed record ValueControlPointProjection(
        Guid JobId,
        Guid CategoryId,
        Guid ControlPointId,
        bool IsIrrelevant,
        bool IsRequired,
        bool IsChecked);
}

public sealed record ValueAnalyticsModelResponse(
    int SchemaVersion,
    string MetricDefinitionVersion,
    DateTimeOffset GeneratedAt,
    string Environment,
    string Scope,
    int WindowDays,
    IReadOnlyList<ValueOrganizationRow> DimOrganization,
    IReadOnlyList<ValueEmployeeRow> DimEmployee,
    IReadOnlyList<ValueJobRow> FactJobs,
    IReadOnlyList<ValueControlPointResultRow> FactControlPointResults,
    ValueSourceCoverage SourceCoverage);

public sealed record ValueOrganizationRow(
    string OrganizationId,
    string OrganizationName,
    int CreatedDateKey);

public sealed record ValueEmployeeRow(
    string EmployeeId,
    string OrganizationId,
    string EmployeeName,
    string Role);

public sealed record ValueJobRow(
    string JobId,
    string OrganizationId,
    string? EmployeeId,
    int? CompletedDateKey,
    string Status,
    bool IsCompliant,
    int ControlPointsRequired,
    int ControlPointsCompleted,
    int MissingDocumentationCount,
    int? PhotosCount,
    int RejectedCount,
    bool ReworkRequired,
    long? CompletionMinutes);

public sealed record ValueControlPointResultRow(
    string ResultId,
    string JobId,
    string OrganizationId,
    int DateKey,
    string Result,
    int? EvidenceCount,
    bool WasCorrected);

public sealed record ValueSourceCoverage(
    string ProductValue,
    string Subscriptions,
    string SalesPipeline,
    string CustomerValueSurvey,
    string LeadSource);
