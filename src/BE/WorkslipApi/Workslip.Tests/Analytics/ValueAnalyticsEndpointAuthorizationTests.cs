using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Workslip.Api.Endpoints;
using Xunit;

namespace Workslip.Tests.Analytics;

public sealed class ValueAnalyticsEndpointAuthorizationTests
{
    [Fact]
    public async Task ValueModelEndpoint_RequiresSuperAdminPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        await using var app = builder.Build();
        app.MapValueAnalyticsEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(route => route.RoutePattern.RawText == "/api/superadmin/analytics/value-model");

        var policies = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(metadata => metadata.Policy)
            .Where(policy => policy is not null)
            .ToList();

        Assert.Contains(AuthPolicies.RequireSuperAdmin, policies);
        Assert.DoesNotContain(AuthPolicies.RequireAdmin, policies);
    }
}
