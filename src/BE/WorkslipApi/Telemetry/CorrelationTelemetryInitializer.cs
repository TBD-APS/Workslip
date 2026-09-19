using System.Security.Claims;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Workslip.Api.Telemetry;

public sealed class CorrelationTelemetryInitializer(IHttpContextAccessor httpContextAccessor) : ITelemetryInitializer
{
    private const string EconomicCallbackPath = "/api/accounting/economic/callback";

    // Same claim CurrentUserContext reads. It is duplicated rather than shared
    // because this initializer is a singleton and ICurrentUserContext is scoped;
    // injecting the scoped service here would be a captive dependency.
    private const string OrganizationIdClaim = "organizationId";

    public void Initialize(ITelemetry telemetry)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return;

        var correlationId = httpContext.Items["CorrelationId"]?.ToString();
        if (!string.IsNullOrWhiteSpace(correlationId))
            telemetry.Context.GlobalProperties["CorrelationId"] = correlationId;

        // Telemetry is read out of Azure Monitor by the platform rather than
        // delivered to it (ADR 0020), so the dimension a consumer needs to answer
        // "which customer is affected" has to be present at emission. This is the
        // organization, deliberately not the user: it identifies a tenant rather
        // than a person, so it does not turn every trace into personal data.
        // Only a well-formed value is emitted, so a malformed claim cannot inject
        // arbitrary text into a dimension the platform groups by.
        var organizationId = httpContext.User?.FindFirstValue(OrganizationIdClaim);
        if (Guid.TryParse(organizationId, out var organization))
            telemetry.Context.GlobalProperties["OrganizationId"] = organization.ToString();

        if (telemetry is not RequestTelemetry requestTelemetry)
            return;

        requestTelemetry.Name = $"{httpContext.Request.Method} {httpContext.Request.Path}";

        // e-conomic's documented redirect returns AgreementGrantToken as `?token=...`.
        // Request telemetry must never persist that query string.
        if (string.Equals(httpContext.Request.Path.Value, EconomicCallbackPath, StringComparison.OrdinalIgnoreCase))
        {
            requestTelemetry.Url = new Uri($"{httpContext.Request.Scheme}://{httpContext.Request.Host}{EconomicCallbackPath}");
        }
    }
}
