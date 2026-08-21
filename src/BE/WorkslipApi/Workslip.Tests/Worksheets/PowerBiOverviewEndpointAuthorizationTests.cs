using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Workslip.Api.Endpoints;
using Xunit;

namespace Workslip.Tests.Worksheets;

public sealed class PowerBiOverviewEndpointAuthorizationTests
{
    [Fact]
    public async Task JobStatusOverviewEndpoint_RequiresAdminPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        await using var app = builder.Build();
        app.MapPowerBiOverviewEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(route => route.RoutePattern.RawText == "/api/power-bi/overview/job-status");

        var policies = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(metadata => metadata.Policy)
            .Where(policy => policy is not null)
            .ToList();

        Assert.Contains(AuthPolicies.RequireAdmin, policies);
    }

    [Fact]
    public async Task JobStatusOverviewEndpoint_DoesNotAcceptOrganizationIdInRoute()
    {
        var builder = WebApplication.CreateBuilder();
        await using var app = builder.Build();
        app.MapPowerBiOverviewEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(route => route.RoutePattern.RawText == "/api/power-bi/overview/job-status");

        Assert.DoesNotContain("organization", endpoint.RoutePattern.RawText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
