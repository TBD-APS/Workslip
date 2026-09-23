using Workslip.Domain;

namespace Workslip.Application.Analytics;

public static class ActivationMetricSemantics
{
    public const string DefinitionVersion = "2026-09-23.v1";

    public static DateTimeOffset? ResolveFirstCompletedCompliantAt(
        string currentStatus,
        DateTimeOffset updatedAt,
        IEnumerable<JobStatusChangeObservation> statusChanges)
    {
        var firstApproval = statusChanges
            .Where(change =>
                string.Equals(change.AfterStatus, JobStatus.Approved.ToString(), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(change.BeforeStatus, JobStatus.Approved.ToString(), StringComparison.OrdinalIgnoreCase))
            .Select(change => (DateTimeOffset?)change.OccurredAt)
            .OrderBy(value => value)
            .FirstOrDefault();

        if (firstApproval.HasValue)
            return firstApproval.Value;

        // Legacy jobs can predate audit history. Current Approved state is still canonical
        // completion evidence, but UpdatedAt is only a fallback timestamp for that legacy case.
        return string.Equals(currentStatus, JobStatus.Approved.ToString(), StringComparison.OrdinalIgnoreCase)
            ? updatedAt
            : null;
    }

    public static double? Percentile(IEnumerable<double> values, double percentile)
    {
        if (percentile is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(percentile));

        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
            return null;

        var index = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }

    public static DateTimeOffset StartOfIsoWeek(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        var daysFromMonday = ((int)utc.DayOfWeek + 6) % 7;
        return new DateTimeOffset(utc.Date.AddDays(-daysFromMonday), TimeSpan.Zero);
    }
}

public sealed record JobStatusChangeObservation(
    string? BeforeStatus,
    string? AfterStatus,
    DateTimeOffset OccurredAt);
