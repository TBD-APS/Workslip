using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Workslip.Api.Endpoints;
using Xunit;

namespace Workslip.Tests.Worksheets;

public sealed class WorksheetReportEndpointAuthorizationTests
{
    [Fact]
    public async Task PowerBiReportLinkEndpoint_RequiresAdminPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        await using var app = builder.Build();
        app.MapWorkSheetEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(route => route.RoutePattern.RawText == "/api/worksheets/all/report/power-bi");

        var policies = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(metadata => metadata.Policy)
            .Where(policy => policy is not null)
            .ToList();

        Assert.Contains(AuthPolicies.RequireAdmin, policies);
    }
}
