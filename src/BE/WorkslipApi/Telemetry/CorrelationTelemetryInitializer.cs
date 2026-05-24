using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Workslip.Api.Telemetry;

public sealed class CorrelationTelemetryInitializer(IHttpContextAccessor httpContextAccessor) : ITelemetryInitializer
{
    public void Initialize(ITelemetry telemetry)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return;

        var correlationId = httpContext.Items["CorrelationId"]?.ToString();
        if (string.IsNullOrWhiteSpace(correlationId))
            return;

        telemetry.Context.GlobalProperties["CorrelationId"] = correlationId;

        if (telemetry is RequestTelemetry requestTelemetry)
        {
            requestTelemetry.Name = $"{httpContext.Request.Method} {httpContext.Request.Path}";
        }
    }
}
