using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ardalis.Result;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Workslip.Application.Diagnostics;

namespace Workslip.Infrastructure.Diagnostics;

public sealed class ApplicationInsightsErrorDiagnosticsService(
    HttpClient httpClient,
    TokenCredential credential,
    IConfiguration configuration,
    ILogger<ApplicationInsightsErrorDiagnosticsService> logger) : IErrorDiagnosticsService
{
    private const string WorkspaceConfigurationKey = "Azure:ApplicationInsights:WorkspaceId";
    private const string LogAnalyticsScope = "https://api.loganalytics.io/.default";
    private const int MaxRawRows = 500;

    private static readonly IReadOnlyDictionary<string, string> AllowedRanges =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["1h"] = "PT1H",
            ["24h"] = "P1D",
            ["7d"] = "P7D"
        };

    private static readonly HashSet<string> AllowedSources =
        new(StringComparer.OrdinalIgnoreCase) { "all", "frontend", "backend" };

    private const string SummaryQuery = """
        let FrontendErrors = AppExceptions
            | extend PropertiesBag = todynamic(Properties)
            | where isnotempty(tostring(PropertiesBag["source"]))
            | project TimeGenerated, Source = "frontend";
        let BackendErrors = AppTraces
            | where SeverityLevel >= 3
            | project TimeGenerated, Source = "backend";
        union isfuzzy=true FrontendErrors, BackendErrors
        | where TimeGenerated >= ago(7d)
        | summarize
            LastHour = countif(TimeGenerated >= ago(1h)),
            Last24Hours = countif(TimeGenerated >= ago(24h)),
            Last7Days = count(),
            FrontendLast24Hours = countif(TimeGenerated >= ago(24h) and Source == "frontend"),
            BackendLast24Hours = countif(TimeGenerated >= ago(24h) and Source == "backend")
        """;

    public async Task<Result<ErrorDiagnosticsDashboard>> GetAsync(
        ErrorDiagnosticsQuery query,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = Validate(query);
        if (validationErrors.Count > 0)
            return Result<ErrorDiagnosticsDashboard>.Invalid(validationErrors);

        var workspaceId = configuration[WorkspaceConfigurationKey]?.Trim();
        if (string.IsNullOrWhiteSpace(workspaceId))
            return Result<ErrorDiagnosticsDashboard>.Success(
                ErrorDiagnosticsDashboard.Unavailable("not_configured"));

        try
        {
            var accessToken = await credential.GetTokenAsync(
                new TokenRequestContext([LogAnalyticsScope]),
                cancellationToken);

            var summaryDocument = await ExecuteQueryAsync(
                workspaceId,
                SummaryQuery,
                "P7D",
                accessToken.Token,
                cancellationToken);

            var detailsDocument = await ExecuteQueryAsync(
                workspaceId,
                BuildDetailsQuery(query.Source),
                AllowedRanges[query.Range],
                accessToken.Token,
                cancellationToken);

            using (summaryDocument)
            using (detailsDocument)
            {
                var summary = ParseSummary(summaryDocument.RootElement);
                var items = ParseItems(detailsDocument.RootElement, query.Limit);

                return Result<ErrorDiagnosticsDashboard>.Success(new ErrorDiagnosticsDashboard(
                    true,
                    null,
                    DateTimeOffset.UtcNow,
                    summary,
                    items));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("Application Insights diagnostics query timed out.");
            return Result<ErrorDiagnosticsDashboard>.Success(
                ErrorDiagnosticsDashboard.Unavailable("timeout"));
        }
        catch (DiagnosticsQueryException exception)
        {
            logger.LogWarning(
                "Application Insights diagnostics query was unavailable. Category={Category} StatusCode={StatusCode}",
                exception.Category,
                exception.StatusCode);
            return Result<ErrorDiagnosticsDashboard>.Success(
                ErrorDiagnosticsDashboard.Unavailable(exception.Category));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Application Insights diagnostics query failed.");
            return Result<ErrorDiagnosticsDashboard>.Success(
                ErrorDiagnosticsDashboard.Unavailable("query_failed"));
        }
    }

    private async Task<JsonDocument> ExecuteQueryAsync(
        string workspaceId,
        string query,
        string timespan,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1/workspaces/{Uri.EscapeDataString(workspaceId)}/query");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Prefer", "wait=10");
        request.Content = JsonContent.Create(new { query, timespan });

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new DiagnosticsQueryException(ToAvailabilityCategory(response.StatusCode), response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static List<ValidationError> Validate(ErrorDiagnosticsQuery query)
    {
        var errors = new List<ValidationError>();

        if (!AllowedRanges.ContainsKey(query.Range))
        {
            errors.Add(new ValidationError
            {
                Identifier = nameof(query.Range),
                ErrorMessage = "Tidsrummet skal være 1h, 24h eller 7d."
            });
        }

        if (!AllowedSources.Contains(query.Source))
        {
            errors.Add(new ValidationError
            {
                Identifier = nameof(query.Source),
                ErrorMessage = "Kilden skal være all, frontend eller backend."
            });
        }

        if (query.Limit is < 10 or > 100)
        {
            errors.Add(new ValidationError
            {
                Identifier = nameof(query.Limit),
                ErrorMessage = "Antallet skal være mellem 10 og 100."
            });
        }

        return errors;
    }

    private static string BuildDetailsQuery(string source)
    {
        var normalizedSource = source.ToLowerInvariant();

        return $$"""
            let FrontendErrors = AppExceptions
                | extend PropertiesBag = todynamic(Properties)
                | where isnotempty(tostring(PropertiesBag["source"]))
                | project
                    Timestamp = TimeGenerated,
                    Source = "frontend",
                    Severity = "error",
                    ErrorType = coalesce(tostring(ProblemId), tostring(ExceptionType), "FrontendError"),
                    Message = coalesce(tostring(OuterMessage), tostring(InnermostMessage), "Frontend error"),
                    Route = tostring(PropertiesBag["route"]),
                    Operation = tostring(OperationName),
                    Release = tostring(PropertiesBag["release"]),
                    CorrelationId = tostring(PropertiesBag["correlationId"]),
                    TraceId = tostring(OperationId);
            let BackendErrors = AppTraces
                | where SeverityLevel >= 3
                | extend PropertiesBag = todynamic(Properties)
                | project
                    Timestamp = TimeGenerated,
                    Source = "backend",
                    Severity = iif(SeverityLevel >= 4, "critical", "error"),
                    ErrorType = coalesce(tostring(PropertiesBag["SourceContext"]), "BackendError"),
                    Message = coalesce(tostring(PropertiesBag["MessageTemplate"]), tostring(Message), "Backend error"),
                    Route = coalesce(tostring(PropertiesBag["Path"]), tostring(PropertiesBag["RequestPath"])),
                    Operation = tostring(OperationName),
                    Release = coalesce(tostring(PropertiesBag["Release"]), tostring(AppVersion)),
                    CorrelationId = coalesce(tostring(PropertiesBag["CorrelationId"]), tostring(PropertiesBag["correlationId"])),
                    TraceId = coalesce(tostring(PropertiesBag["TraceId"]), tostring(OperationId));
            union isfuzzy=true FrontendErrors, BackendErrors
            | where "{{normalizedSource}}" == "all" or Source == "{{normalizedSource}}"
            | top {{MaxRawRows}} by Timestamp desc
            """;
    }

    private static ErrorDiagnosticsSummary ParseSummary(JsonElement root)
    {
        var row = ReadFirstRow(root);
        return new ErrorDiagnosticsSummary(
            ReadInt(row, "LastHour"),
            ReadInt(row, "Last24Hours"),
            ReadInt(row, "Last7Days"),
            ReadInt(row, "FrontendLast24Hours"),
            ReadInt(row, "BackendLast24Hours"));
    }

    private static IReadOnlyList<ErrorDiagnosticsItem> ParseItems(JsonElement root, int limit)
    {
        var normalized = ReadRows(root)
            .Select(ParseRawError)
            .Where(error => error is not null)
            .Select(error => Sanitize(error!))
            .ToArray();

        return normalized
            .GroupBy(item => item.Fingerprint, StringComparer.Ordinal)
            .Select(group =>
            {
                var latest = group.OrderByDescending(item => item.TimestampUtc).First();
                return latest with { Occurrences = group.Count() };
            })
            .OrderByDescending(item => item.TimestampUtc)
            .Take(limit)
            .ToArray();
    }

    private static RawError? ParseRawError(IReadOnlyDictionary<string, JsonElement> row)
    {
        var timestampText = ReadString(row, "Timestamp");
        if (!DateTimeOffset.TryParse(timestampText, out var timestamp))
            return null;

        return new RawError(
            timestamp,
            ReadString(row, "Source") ?? "backend",
            ReadString(row, "Severity") ?? "error",
            ReadString(row, "ErrorType") ?? "Error",
            ReadString(row, "Message") ?? "Ukendt fejl",
            ReadString(row, "Route"),
            ReadString(row, "Operation"),
            ReadString(row, "Release"),
            ReadString(row, "CorrelationId"),
            ReadString(row, "TraceId"));
    }

    private static ErrorDiagnosticsItem Sanitize(RawError error)
    {
        var source = string.Equals(error.Source, "frontend", StringComparison.OrdinalIgnoreCase)
            ? "frontend"
            : "backend";
        var severity = string.Equals(error.Severity, "critical", StringComparison.OrdinalIgnoreCase)
            ? "critical"
            : "error";
        var errorType = DiagnosticsSanitizer.SanitizeField(error.ErrorType) ?? "Error";
        var message = DiagnosticsSanitizer.SanitizeMessage(error.Message);
        var route = DiagnosticsSanitizer.SanitizeRoute(error.Route);
        var operation = DiagnosticsSanitizer.SanitizeRoute(error.Operation);
        var release = DiagnosticsSanitizer.SanitizeField(error.Release);
        var correlationId = DiagnosticsSanitizer.SanitizeCorrelationId(error.CorrelationId);
        var traceId = DiagnosticsSanitizer.SanitizeCorrelationId(error.TraceId);
        var fingerprint = DiagnosticsSanitizer.Fingerprint(
            source,
            errorType,
            message,
            route,
            operation,
            release);

        return new ErrorDiagnosticsItem(
            error.TimestampUtc,
            source,
            severity,
            errorType,
            fingerprint,
            message,
            route,
            operation,
            release,
            correlationId,
            traceId,
            1);
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadFirstRow(JsonElement root) =>
        ReadRows(root).FirstOrDefault()
        ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> ReadRows(JsonElement root)
    {
        if (!root.TryGetProperty("tables", out var tables)
            || tables.ValueKind != JsonValueKind.Array
            || tables.GetArrayLength() == 0)
        {
            return [];
        }

        var table = tables[0];
        if (!table.TryGetProperty("columns", out var columns)
            || !table.TryGetProperty("rows", out var rows)
            || columns.ValueKind != JsonValueKind.Array
            || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = columns
            .EnumerateArray()
            .Select(column => column.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty)
            .ToArray();

        var result = new List<IReadOnlyDictionary<string, JsonElement>>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array)
                continue;

            var values = row.EnumerateArray().ToArray();
            var mapped = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < names.Length && index < values.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(names[index]))
                    mapped[names[index]] = values[index];
            }
            result.Add(mapped);
        }

        return result;
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement> row, string name)
    {
        if (!row.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static int ReadInt(IReadOnlyDictionary<string, JsonElement> row, string name)
    {
        if (!row.TryGetValue(name, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        return int.TryParse(ReadString(row, name), out number) ? number : 0;
    }

    private static string ToAvailabilityCategory(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "permission_denied",
        HttpStatusCode.TooManyRequests => "throttled",
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "timeout",
        _ => "query_failed"
    };

    private sealed record RawError(
        DateTimeOffset TimestampUtc,
        string Source,
        string Severity,
        string ErrorType,
        string Message,
        string? Route,
        string? Operation,
        string? Release,
        string? CorrelationId,
        string? TraceId);

    private sealed class DiagnosticsQueryException(string category, HttpStatusCode statusCode) : Exception
    {
        public string Category { get; } = category;
        public HttpStatusCode StatusCode { get; } = statusCode;
    }
}
