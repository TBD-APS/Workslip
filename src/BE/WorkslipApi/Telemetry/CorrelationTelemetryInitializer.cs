using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Workslip.Api.Telemetry;

public sealed class CorrelationTelemetryInitializer(IHttpContextAccessor httpContextAccessor) : ITelemetryInitializer
{
    private const string EconomicCallbackPath = "/api/accounting/economic/callback";

    public void Initialize(ITelemetry telemetry)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return;

        var correlationId = httpContext.Items["CorrelationId"]?.ToString();
        if (!string.IsNullOrWhiteSpace(correlationId))
            telemetry.Context.GlobalProperties["CorrelationId"] = correlationId;

        if (telemetry is not RequestTelemetry requestTelemetry)
            return;

        requestTelemetry.Name = $"{httpContext.Request.Method} {httpContext.Request.Path}";

        // e-conomic's documented redirect returns AgreementGrantToken as `?token=...`.
        // Request telemetry must never persist that query string.
        if (httpContext.Request.Path.Equals(EconomicCallbackPath, StringComparison.OrdinalIgnoreCase))
        {
            requestTelemetry.Url = new Uri($"{httpContext.Request.Scheme}://{httpContext.Request.Host}{EconomicCallbackPath}");
        }
    }
}
