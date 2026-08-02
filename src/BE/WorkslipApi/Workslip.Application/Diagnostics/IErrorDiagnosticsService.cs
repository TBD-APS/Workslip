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
    string? AvailabilityReason,
    DateTimeOffset GeneratedAtUtc,
    ErrorDiagnosticsSummary Summary,
    IReadOnlyList<ErrorDiagnosticsItem> Items)
{
    public static ErrorDiagnosticsDashboard Unavailable(string reason) =>
        new(
            false,
            reason,
            DateTimeOffset.UtcNow,
            new ErrorDiagnosticsSummary(0, 0, 0, 0, 0),
            []);
}

public sealed record ErrorDiagnosticsSummary(
    int LastHour,
    int Last24Hours,
    int Last7Days,
    int FrontendLast24Hours,
    int BackendLast24Hours);

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
    int Occurrences);
