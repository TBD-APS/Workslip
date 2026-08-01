using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Workslip.Application.Common;

namespace Workslip.Api.Telemetry;

public sealed class TelemetryCacheDiagnostics(
    ICacheDiagnostics inner,
    TelemetryClient? telemetryClient) : ICacheDiagnostics
{
    public void RecordHit(string region)
    {
        inner.RecordHit(region);
        TrackMetric("workslip.cache.hit", 1, region);
    }

    public void RecordMiss(string region)
    {
        inner.RecordMiss(region);
        TrackMetric("workslip.cache.miss", 1, region);
    }

    public void RecordSet(string region)
    {
        inner.RecordSet(region);
        TrackMetric("workslip.cache.set", 1, region);
    }

    public void RecordInvalidation(string region)
    {
        inner.RecordInvalidation(region);
        TrackMetric("workslip.cache.invalidation", 1, region);
    }

    public void RecordFailure(string region)
    {
        inner.RecordFailure(region);
        TrackMetric("workslip.cache.failure", 1, region);
    }

    public void RecordLoad(string region, TimeSpan duration)
    {
        inner.RecordLoad(region, duration);
        TrackMetric("workslip.cache.load_duration_ms", duration.TotalMilliseconds, region);
    }

    public void RecordGlobalClear()
    {
        inner.RecordGlobalClear();

        if (telemetryClient is null)
        {
            return;
        }

        foreach (var region in inner.GetSnapshot().Regions)
        {
            TrackMetric("workslip.cache.invalidation", 1, region.Name);
        }

        telemetryClient.TrackEvent(new EventTelemetry("workslip.cache.global_clear"));
    }

    public CacheDiagnosticsSnapshot GetSnapshot() => inner.GetSnapshot();

    private void TrackMetric(string name, double value, string region)
    {
        if (telemetryClient is null)
        {
            return;
        }

        var telemetry = new MetricTelemetry(name, value);
        telemetry.Properties["region"] = region;
        telemetryClient.TrackMetric(telemetry);
    }
}
