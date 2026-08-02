using Ardalis.Result;

namespace Workslip.Application.Diagnostics;

public interface IErrorDiagnosticsService
{
    Task<Result<ErrorDiagnosticsDashboard>> GetAsync(
        ErrorDiagnosticsQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record ErrorDiagnosticsQuery(
    string Range = "24h",
    string Source = "all",
    int Limit = 50);

public sealed record ErrorDiagnosticsDashboard(
    bool IsAvailable,
    bool IsComplete,
    bool IsStale,
    string? AvailabilityReason,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? DataRetrievedAtUtc,
    bool SummaryAvailable,
    bool ItemsAvailable,
    bool HasPartialAzureResults,
    bool IsTruncated,
    ErrorDiagnosticsSummary? Summary,
    IReadOnlyList<ErrorDiagnosticsItem> Items)
{
    public static ErrorDiagnosticsDashboard Unavailable(string reason) =>
        new(
            false,
            false,
            false,
            reason,
            DateTimeOffset.UtcNow,
            null,
            false,
            false,
            false,
            false,
            null,
            []);

    public ErrorDiagnosticsDashboard AsStale(string reason) =>
        this with
        {
            IsAvailable = true,
            IsComplete = false,
            IsStale = true,
            AvailabilityReason = reason,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
}

public sealed record ErrorDiagnosticsSummary(
    long LastHour,
    long Last24Hours,
    long Last7Days,
    long FrontendLast24Hours,
    long BackendLast24Hours);

public sealed record ErrorDiagnosticsItem(
    DateTimeOffset TimestampUtc,
    string Source,
    string Severity,
    string ErrorType,
    string Fingerprint,
    string Message,
    string? Route,
    string? Operation,
    string? Release,
    string? CorrelationId,
    string? TraceId,
    long Occurrences);
