using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ardalis.Result;
using Azure.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Workslip.Application.Diagnostics;

namespace Workslip.Infrastructure.Diagnostics;

public sealed class ApplicationInsightsErrorDiagnosticsService(
    HttpClient httpClient,
    TokenCredential credential,
    IConfiguration configuration,
    IMemoryCache memoryCache,
    ILogger<ApplicationInsightsErrorDiagnosticsService> logger) : IErrorDiagnosticsService
{
    private const string WorkspaceConfigurationKey = "Azure:ApplicationInsights:WorkspaceId";
    private const string LogAnalyticsScope = "https://api.loganalytics.io/.default";
    private const string CacheKeyPrefix = "application-insights-errors";
    private const int MaxRawGroups = 1_000;
    private const int MaxQueryAttempts = 2;
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromHours(1);

    private static readonly IReadOnlyDictionary<string, string> AllowedRanges =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["1h"] = "PT1H",
            ["24h"] = "P1D",
            ["7d"] = "P7D"
        };

    private static readonly HashSet<string> AllowedSources =
        new(StringComparer.OrdinalIgnoreCase) { "all", "frontend", "backend" };

    public async Task<Result<ErrorDiagnosticsDashboard>> GetAsync(
        ErrorDiagnosticsQuery query,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = Validate(query);
        if (validationErrors.Count > 0)
            return Result<ErrorDiagnosticsDashboard>.Invalid(validationErrors);

        var normalizedRange = query.Range.ToLowerInvariant();
        var normalizedSource = query.Source.ToLowerInvariant();
        var workspaceId = configuration[WorkspaceConfigurationKey]?.Trim();
        var cacheKey = $"{CacheKeyPrefix}:{workspaceId ?? "unconfigured"}:{normalizedRange}:{normalizedSource}:{query.Limit}";

        if (string.IsNullOrWhiteSpace(workspaceId))
            return Result<ErrorDiagnosticsDashboard>.Success(GetFallback(cacheKey, "not_configured"));

        AccessToken accessToken;
        try
        {
            accessToken = await credential.GetTokenAsync(
                new TokenRequestContext([LogAnalyticsScope]),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Application Insights diagnostics could not acquire an Azure token. ExceptionType={ExceptionType}",
                exception.GetType().Name);
            return Result<ErrorDiagnosticsDashboard>.Success(GetFallback(cacheKey, "token_unavailable"));
        }

        var summaryTask = ExecuteSectionAsync(
            "summary",
            workspaceId,
            BuildSummaryQuery(normalizedSource),
            "P7D",
            accessToken.Token,
            ParseSummary,
            cancellationToken);
        var itemsTask = ExecuteSectionAsync(
            "items",
            workspaceId,
            BuildDetailsQuery(normalizedSource),
            AllowedRanges[normalizedRange],
            accessToken.Token,
            root => ParseItems(root, query.Limit),
            cancellationToken);
        var telemetryHealthTask = ExecuteSectionAsync(
            "telemetry-health",
            workspaceId,
            BuildTelemetryHealthQuery(),
            "P7D",
            accessToken.Token,
            ParseTelemetryHealth,
            cancellationToken);

        await Task.WhenAll(summaryTask, itemsTask, telemetryHealthTask);

        var summarySection = await summaryTask;
        var itemsSection = await itemsTask;
        var telemetryHealthSection = await telemetryHealthTask;
        var now = DateTimeOffset.UtcNow;
        var summaryAvailable = summarySection.HasValue;
        var itemsAvailable = itemsSection.HasValue;
        var telemetryHealthAvailable = telemetryHealthSection.HasValue;
        var hasPartialAzureResults =
            summarySection.IsPartial
            || itemsSection.IsPartial
            || telemetryHealthSection.IsPartial;
        var isComplete =
            summaryAvailable
            && itemsAvailable
            && telemetryHealthAvailable
            && !hasPartialAzureResults;
        var reason = CombineReasons(
            summarySection.Reason,
            itemsSection.Reason,
            telemetryHealthSection.Reason);

        if (isComplete)
        {
            var dashboard = new ErrorDiagnosticsDashboard(
                true,
                true,
                false,
                null,
                now,
                now,
                true,
                true,
                true,
                false,
                itemsSection.Value!.IsTruncated,
                summarySection.Value,
                telemetryHealthSection.Value,
                itemsSection.Value.Items);

            memoryCache.Set(cacheKey, dashboard, SnapshotLifetime);
            return Result<ErrorDiagnosticsDashboard>.Success(dashboard);
        }

        if (memoryCache.TryGetValue<ErrorDiagnosticsDashboard>(cacheKey, out var cached)
            && cached is not null)
        {
            return Result<ErrorDiagnosticsDashboard>.Success(
                cached.AsStale(reason ?? "query_failed"));
        }

        var anySectionAvailable =
            summaryAvailable
            || itemsAvailable
            || telemetryHealthAvailable;
        var partialDashboard = new ErrorDiagnosticsDashboard(
            anySectionAvailable,
            false,
            false,
            reason ?? "query_failed",
            now,
            anySectionAvailable ? now : null,
            summaryAvailable,
            itemsAvailable,
            telemetryHealthAvailable,
            hasPartialAzureResults,
            itemsSection.Value?.IsTruncated ?? false,
            summarySection.Value,
            telemetryHealthSection.Value,
            itemsSection.Value?.Items ?? []);

        return Result<ErrorDiagnosticsDashboard>.Success(partialDashboard);
    }

    private ErrorDiagnosticsDashboard GetFallback(string cacheKey, string reason)
    {
        if (memoryCache.TryGetValue<ErrorDiagnosticsDashboard>(cacheKey, out var cached)
            && cached is not null)
        {
            return cached.AsStale(reason);
        }

        return ErrorDiagnosticsDashboard.Unavailable(reason);
    }

    private async Task<QuerySection<T>> ExecuteSectionAsync<T>(
        string sectionName,
        string workspaceId,
        string query,
        string timespan,
        string accessToken,
        Func<JsonElement, T> parser,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var response = await ExecuteQueryAsync(
                workspaceId,
                query,
                timespan,
                accessToken,
                cancellationToken);

            var value = parser(response.Document.RootElement);
            return new QuerySection<T>(true, value, response.IsPartial, response.IsPartial ? "partial_result" : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("Application Insights diagnostics section timed out. Section={Section}", sectionName);
            return QuerySection<T>.Failure("timeout");
        }
        catch (DiagnosticsQueryException exception)
        {
            logger.LogWarning(
                "Application Insights diagnostics section was unavailable. Section={Section} Category={Category} StatusCode={StatusCode}",
                sectionName,
                exception.Category,
                exception.StatusCode);
            return QuerySection<T>.Failure(exception.Category);
        }
        catch (DiagnosticsResponseException)
        {
            logger.LogWarning("Application Insights returned an invalid diagnostics schema. Section={Section}", sectionName);
            return QuerySection<T>.Failure("invalid_response");
        }
        catch (JsonException)
        {
            logger.LogWarning("Application Insights returned invalid JSON. Section={Section}", sectionName);
            return QuerySection<T>.Failure("invalid_response");
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Application Insights diagnostics section failed. Section={Section}",
                sectionName);
            return QuerySection<T>.Failure("query_failed");
        }
    }

    private async Task<QueryResponse> ExecuteQueryAsync(
        string workspaceId,
        string query,
        string timespan,
        string accessToken,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxQueryAttempts; attempt++)
        {
            try
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
                {
                    var statusCode = response.StatusCode;
                    if (attempt < MaxQueryAttempts && IsTransient(statusCode))
                    {
                        await Task.Delay(GetRetryDelay(response), cancellationToken);
                        continue;
                    }

                    throw new DiagnosticsQueryException(ToAvailabilityCategory(statusCode), statusCode);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var isPartial = ReadPartialResultState(document.RootElement);
                return new QueryResponse(document, isPartial);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch (HttpRequestException) when (attempt < MaxQueryAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }

        throw new DiagnosticsQueryException("query_failed", HttpStatusCode.ServiceUnavailable);
    }

    private static bool ReadPartialResultState(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error))
            return false;

        if (error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty("code", out var code)
            || code.ValueKind != JsonValueKind.String)
        {
            throw new DiagnosticsResponseException();
        }

        if (string.Equals(code.GetString(), "PartialError", StringComparison.OrdinalIgnoreCase))
            return true;

        throw new DiagnosticsResponseException();
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

    private static string BuildSummaryQuery(string source) => $$"""
        let RequestedSource = "{{source}}";
        let FrontendErrors = AppExceptions
            | extend PropertiesBag = todynamic(Properties)
            | where isnotempty(tostring(PropertiesBag["source"]))
            | project TimeGenerated, Source = "frontend", Weight = tolong(coalesce(ItemCount, 1));
        let ExplicitBackendErrors = AppTraces
            | where SeverityLevel >= 3
            | project TimeGenerated, Source = "backend", Weight = tolong(coalesce(ItemCount, 1)), OperationId;
        let ExplicitOperationIds = ExplicitBackendErrors
            | where isnotempty(OperationId)
            | distinct OperationId;
        let BackendRequestFailures = AppTraces
            | extend PropertiesBag = todynamic(Properties)
            | extend HttpStatusCode = toint(PropertiesBag["StatusCode"])
            | where HttpStatusCode >= 500
            | where isempty(OperationId) or OperationId !in (ExplicitOperationIds)
            | project TimeGenerated, Source = "backend", Weight = tolong(coalesce(ItemCount, 1));
        union FrontendErrors, ExplicitBackendErrors, BackendRequestFailures
        | where TimeGenerated >= ago(7d)
        | where RequestedSource == "all" or Source == RequestedSource
        | summarize
            LastHour = sumif(Weight, TimeGenerated >= ago(1h)),
            Last24Hours = sumif(Weight, TimeGenerated >= ago(24h)),
            Last7Days = sum(Weight),
            FrontendLast24Hours = sumif(Weight, TimeGenerated >= ago(24h) and Source == "frontend"),
            BackendLast24Hours = sumif(Weight, TimeGenerated >= ago(24h) and Source == "backend")
        """;

    private static string BuildDetailsQuery(string source) => $$"""
        let RequestedSource = "{{source}}";
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
                TraceId = tostring(OperationId),
                Weight = tolong(coalesce(ItemCount, 1));
        let ExplicitBackendErrors = AppTraces
            | where SeverityLevel >= 3
            | extend PropertiesBag = todynamic(Properties)
            | project
                Timestamp = TimeGenerated,
                Source = "backend",
                Severity = iif(SeverityLevel >= 4, "critical", "error"),
                ErrorType = coalesce(tostring(PropertiesBag["ExceptionType"]), tostring(PropertiesBag["SourceContext"]), "BackendError"),
                Message = coalesce(tostring(PropertiesBag["MessageTemplate"]), tostring(Message), "Backend error"),
                Route = coalesce(tostring(PropertiesBag["Path"]), tostring(PropertiesBag["RequestPath"])),
                Operation = tostring(OperationName),
                Release = coalesce(tostring(PropertiesBag["Release"]), tostring(AppVersion)),
                CorrelationId = coalesce(tostring(PropertiesBag["CorrelationId"]), tostring(PropertiesBag["correlationId"])),
                TraceId = coalesce(tostring(PropertiesBag["TraceId"]), tostring(OperationId)),
                Weight = tolong(coalesce(ItemCount, 1)),
                OperationId;
        let ExplicitOperationIds = ExplicitBackendErrors
            | where isnotempty(OperationId)
            | distinct OperationId;
        let BackendRequestFailures = AppTraces
            | extend PropertiesBag = todynamic(Properties)
            | extend HttpStatusCode = toint(PropertiesBag["StatusCode"])
            | where HttpStatusCode >= 500
            | where isempty(OperationId) or OperationId !in (ExplicitOperationIds)
            | project
                Timestamp = TimeGenerated,
                Source = "backend",
                Severity = "error",
                ErrorType = strcat("HTTP ", tostring(HttpStatusCode)),
                Message = coalesce(tostring(PropertiesBag["MessageTemplate"]), tostring(Message), "Backend request failed"),
                Route = tostring(PropertiesBag["RequestPath"]),
                Operation = tostring(OperationName),
                Release = coalesce(tostring(PropertiesBag["Release"]), tostring(AppVersion)),
                CorrelationId = coalesce(tostring(PropertiesBag["CorrelationId"]), tostring(PropertiesBag["correlationId"])),
                TraceId = coalesce(tostring(PropertiesBag["TraceId"]), tostring(OperationId)),
                Weight = tolong(coalesce(ItemCount, 1));
        union FrontendErrors, ExplicitBackendErrors, BackendRequestFailures
        | where RequestedSource == "all" or Source == RequestedSource
        | summarize
            Occurrences = sum(Weight),
            arg_max(Timestamp, CorrelationId, TraceId)
            by Source, Severity, ErrorType, Message, Route, Operation, Release
        | top {{MaxRawGroups + 1}} by Timestamp desc
        """;

    private static string BuildTelemetryHealthQuery() => """
        print
            FrontendLastSeenUtc = toscalar(
                AppEvents
                | where Name == "telemetry.heartbeat"
                | summarize max(TimeGenerated)),
            BackendLastSeenUtc = toscalar(
                AppRequests
                | summarize max(TimeGenerated))
        """;

    private static ErrorDiagnosticsSummary ParseSummary(JsonElement root)
    {
        var rows = ReadRows(
            root,
            "LastHour",
            "Last24Hours",
            "Last7Days",
            "FrontendLast24Hours",
            "BackendLast24Hours");

        if (rows.Count != 1)
            throw new DiagnosticsResponseException();

        var row = rows[0];
        return new ErrorDiagnosticsSummary(
            ReadRequiredLong(row, "LastHour", allowZero: true),
            ReadRequiredLong(row, "Last24Hours", allowZero: true),
            ReadRequiredLong(row, "Last7Days", allowZero: true),
            ReadRequiredLong(row, "FrontendLast24Hours", allowZero: true),
            ReadRequiredLong(row, "BackendLast24Hours", allowZero: true));
    }

    private static ErrorDiagnosticsTelemetryHealth ParseTelemetryHealth(JsonElement root)
    {
        var rows = ReadRows(root, "FrontendLastSeenUtc", "BackendLastSeenUtc");
        if (rows.Count != 1)
            throw new DiagnosticsResponseException();

        var row = rows[0];
        return new ErrorDiagnosticsTelemetryHealth(
            ReadOptionalTimestamp(row, "FrontendLastSeenUtc"),
            ReadOptionalTimestamp(row, "BackendLastSeenUtc"));
    }

    private static DetailsParseResult ParseItems(JsonElement root, int limit)
    {
        var rows = ReadRows(
            root,
            "Timestamp",
            "Source",
            "Severity",
            "ErrorType",
            "Message",
            "Route",
            "Operation",
            "Release",
            "CorrelationId",
            "TraceId",
            "Occurrences");
        var isTruncated = rows.Count > MaxRawGroups;

        var normalized = rows
            .Take(MaxRawGroups)
            .Select(ParseRawError)
            .Select(Sanitize)
            .ToArray();

        var grouped = normalized
            .GroupBy(item => item.Fingerprint, StringComparer.Ordinal)
            .Select(group =>
            {
                var latest = group.OrderByDescending(item => item.TimestampUtc).First();
                return latest with { Occurrences = SumOccurrences(group) };
            })
            .OrderByDescending(item => item.TimestampUtc)
            .Take(limit)
            .ToArray();

        return new DetailsParseResult(grouped, isTruncated);
    }

    private static RawError ParseRawError(IReadOnlyDictionary<string, JsonElement> row)
    {
        var timestamp = ReadOptionalTimestamp(row, "Timestamp");
        if (timestamp is null)
            throw new DiagnosticsResponseException();

        var source = ReadRequiredString(row, "Source").ToLowerInvariant();
        var severity = ReadRequiredString(row, "Severity").ToLowerInvariant();
        if ((source != "frontend" && source != "backend")
            || (severity != "error" && severity != "critical"))
        {
            throw new DiagnosticsResponseException();
        }

        return new RawError(
            timestamp.Value,
            source,
            severity,
            ReadRequiredString(row, "ErrorType"),
            ReadRequiredString(row, "Message"),
            ReadOptionalString(row, "Route"),
            ReadOptionalString(row, "Operation"),
            ReadOptionalString(row, "Release"),
            ReadOptionalString(row, "CorrelationId"),
            ReadOptionalString(row, "TraceId"),
            ReadRequiredLong(row, "Occurrences", allowZero: false));
    }

    private static ErrorDiagnosticsItem Sanitize(RawError error)
    {
        var errorType = DiagnosticsSanitizer.SanitizeField(error.ErrorType) ?? "Error";
        var message = DiagnosticsSanitizer.SanitizeMessage(error.Message);
        var route = DiagnosticsSanitizer.SanitizeRoute(error.Route);
        var operation = DiagnosticsSanitizer.SanitizeRoute(error.Operation);
        var release = DiagnosticsSanitizer.SanitizeField(error.Release);
        var correlationId = DiagnosticsSanitizer.SanitizeCorrelationId(error.CorrelationId);
        var traceId = DiagnosticsSanitizer.SanitizeCorrelationId(error.TraceId);
        var fingerprint = DiagnosticsSanitizer.Fingerprint(
            error.Source,
            errorType,
            message,
            route,
            operation,
            release);

        return new ErrorDiagnosticsItem(
            error.TimestampUtc,
            error.Source,
            error.Severity,
            errorType,
            fingerprint,
            message,
            route,
            operation,
            release,
            correlationId,
            traceId,
            error.Occurrences);
    }

    private static long SumOccurrences(IEnumerable<ErrorDiagnosticsItem> items)
    {
        var total = 0L;
        foreach (var item in items)
        {
            if (item.Occurrences > long.MaxValue - total)
                return long.MaxValue;

            total += item.Occurrences;
        }

        return total;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> ReadRows(
        JsonElement root,
        params string[] requiredColumns)
    {
        if (!root.TryGetProperty("tables", out var tables)
            || tables.ValueKind != JsonValueKind.Array
            || tables.GetArrayLength() == 0)
        {
            throw new DiagnosticsResponseException();
        }

        var candidates = tables
            .EnumerateArray()
            .Where(table => table.ValueKind == JsonValueKind.Object)
            .ToArray();
        var table = candidates.FirstOrDefault(candidate =>
            candidate.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "PrimaryResult", StringComparison.OrdinalIgnoreCase));

        if (table.ValueKind == JsonValueKind.Undefined && candidates.Length == 1)
            table = candidates[0];

        if (table.ValueKind == JsonValueKind.Undefined
            || !table.TryGetProperty("columns", out var columns)
            || !table.TryGetProperty("rows", out var rows)
            || columns.ValueKind != JsonValueKind.Array
            || rows.ValueKind != JsonValueKind.Array)
        {
            throw new DiagnosticsResponseException();
        }

        var names = columns
            .EnumerateArray()
            .Select(column => column.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString() ?? string.Empty
                : string.Empty)
            .ToArray();
        var availableColumns = names.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (names.Length == 0
            || names.Any(string.IsNullOrWhiteSpace)
            || requiredColumns.Any(required => !availableColumns.Contains(required)))
        {
            throw new DiagnosticsResponseException();
        }

        var result = new List<IReadOnlyDictionary<string, JsonElement>>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array)
                throw new DiagnosticsResponseException();

            var values = row.EnumerateArray().ToArray();
            if (values.Length != names.Length)
                throw new DiagnosticsResponseException();

            var mapped = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < names.Length; index++)
                mapped[names[index]] = values[index];

            result.Add(mapped);
        }

        return result;
    }

    private static string ReadRequiredString(IReadOnlyDictionary<string, JsonElement> row, string name)
    {
        var value = ReadOptionalString(row, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new DiagnosticsResponseException();

        return value;
    }

    private static string? ReadOptionalString(IReadOnlyDictionary<string, JsonElement> row, string name)
    {
        if (!row.TryGetValue(name, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static DateTimeOffset? ReadOptionalTimestamp(
        IReadOnlyDictionary<string, JsonElement> row,
        string name)
    {
        var timestampText = ReadOptionalString(row, name);
        if (string.IsNullOrWhiteSpace(timestampText))
            return null;

        if (!DateTimeOffset.TryParse(
                timestampText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            throw new DiagnosticsResponseException();
        }

        return timestamp;
    }

    private static long ReadRequiredLong(
        IReadOnlyDictionary<string, JsonElement> row,
        string name,
        bool allowZero)
    {
        if (!row.TryGetValue(name, out var value))
            throw new DiagnosticsResponseException();

        long number;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numericValue))
        {
            number = numericValue;
        }
        else if (!long.TryParse(
                     ReadOptionalString(row, name),
                     NumberStyles.Integer,
                     CultureInfo.InvariantCulture,
                     out number))
        {
            throw new DiagnosticsResponseException();
        }

        if (number < 0 || (!allowZero && number == 0))
            throw new DiagnosticsResponseException();

        return number;
    }

    private static string? CombineReasons(params string?[] reasons)
    {
        var values = reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return values.Length == 0 ? null : string.Join(",", values);
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || statusCode == HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null || retryAfter <= TimeSpan.Zero)
            return TimeSpan.FromMilliseconds(250);

        return retryAfter > TimeSpan.FromSeconds(2)
            ? TimeSpan.FromSeconds(2)
            : retryAfter.Value;
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
        string? TraceId,
        long Occurrences);

    private sealed record DetailsParseResult(
        IReadOnlyList<ErrorDiagnosticsItem> Items,
        bool IsTruncated);

    private sealed record QuerySection<T>(
        bool HasValue,
        T? Value,
        bool IsPartial,
        string? Reason)
        where T : class
    {
        public static QuerySection<T> Failure(string reason) =>
            new(false, null, false, reason);
    }

    private sealed class QueryResponse(JsonDocument document, bool isPartial) : IDisposable
    {
        public JsonDocument Document { get; } = document;
        public bool IsPartial { get; } = isPartial;
        public void Dispose() => Document.Dispose();
    }

    private sealed class DiagnosticsQueryException(string category, HttpStatusCode statusCode) : Exception
    {
        public string Category { get; } = category;
        public HttpStatusCode StatusCode { get; } = statusCode;
    }

    private sealed class DiagnosticsResponseException : Exception
    {
    }
}
