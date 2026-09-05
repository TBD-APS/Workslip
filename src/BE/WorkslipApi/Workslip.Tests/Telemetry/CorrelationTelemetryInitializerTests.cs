using System.Security.Claims;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Http;
using Workslip.Api.Telemetry;

namespace Workslip.Tests.Telemetry;

public sealed class CorrelationTelemetryInitializerTests
{
    private const string OrganizationIdClaim = "organizationId";

    [Fact]
    public void Initialize_TagsTelemetryWithTheOrganizationClaim()
    {
        var organizationId = Guid.NewGuid();
        var telemetry = new TraceTelemetry();
        var initializer = CreateInitializer(CreateContext(organizationId.ToString()));

        initializer.Initialize(telemetry);

        Assert.Equal(
            organizationId.ToString(),
            telemetry.Context.GlobalProperties["OrganizationId"]);
    }

    [Fact]
    public void Initialize_TagsNonRequestTelemetrySoTracesCanBeGroupedByTenant()
    {
        // The platform reads telemetry out of Azure Monitor rather than receiving
        // a delivery, so the dimension has to be on every item it can query - not
        // only on request telemetry.
        var organizationId = Guid.NewGuid();
        var exception = new ExceptionTelemetry(new InvalidOperationException("boom"));
        var initializer = CreateInitializer(CreateContext(organizationId.ToString()));

        initializer.Initialize(exception);

        Assert.Equal(
            organizationId.ToString(),
            exception.Context.GlobalProperties["OrganizationId"]);
    }

    [Fact]
    public void Initialize_OmitsOrganizationWhenTheClaimIsAbsent()
    {
        var telemetry = new TraceTelemetry();
        var initializer = CreateInitializer(CreateContext(organizationClaim: null));

        initializer.Initialize(telemetry);

        Assert.False(telemetry.Context.GlobalProperties.ContainsKey("OrganizationId"));
    }

    [Fact]
    public void Initialize_OmitsOrganizationWhenTheClaimIsNotAnIdentifier()
    {
        // A malformed claim must not reach a dimension the platform groups by.
        var telemetry = new TraceTelemetry();
        var initializer = CreateInitializer(CreateContext("not-a-guid; DROP"));

        initializer.Initialize(telemetry);

        Assert.False(telemetry.Context.GlobalProperties.ContainsKey("OrganizationId"));
    }

    [Fact]
    public void Initialize_StillTagsTheCorrelationId()
    {
        const string correlationId = "f93a41e5-5457-463b-b7f2-e37ccca69673";
        var context = CreateContext(organizationClaim: null);
        context.Items["CorrelationId"] = correlationId;
        var telemetry = new TraceTelemetry();
        var initializer = CreateInitializer(context);

        initializer.Initialize(telemetry);

        Assert.Equal(correlationId, telemetry.Context.GlobalProperties["CorrelationId"]);
    }

    [Fact]
    public void Initialize_IgnoresTelemetryRaisedOutsideARequest()
    {
        var telemetry = new TraceTelemetry();
        var initializer = new CorrelationTelemetryInitializer(new HttpContextAccessor());

        initializer.Initialize(telemetry);

        Assert.False(telemetry.Context.GlobalProperties.ContainsKey("OrganizationId"));
        Assert.False(telemetry.Context.GlobalProperties.ContainsKey("CorrelationId"));
    }

    private static DefaultHttpContext CreateContext(string? organizationClaim)
    {
        var context = new DefaultHttpContext();
        if (organizationClaim is not null)
        {
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(OrganizationIdClaim, organizationClaim)], "test"));
        }

        return context;
    }

    private static CorrelationTelemetryInitializer CreateInitializer(HttpContext context) =>
        new(new HttpContextAccessor { HttpContext = context });
}
