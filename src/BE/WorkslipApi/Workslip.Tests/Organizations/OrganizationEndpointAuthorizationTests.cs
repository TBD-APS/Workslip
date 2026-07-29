using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Workslip.Api.Endpoints;
using Xunit;

namespace Workslip.Tests.Organizations;

public sealed class OrganizationEndpointAuthorizationTests
{
    [Fact]
    public async Task OrganizationAdministrationEndpoints_RequireSuperAdminPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        await using var app = builder.Build();
        app.MapOrganizationEndpoints();

        var organizationEndpoints = app.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/organizations", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(2, organizationEndpoints.Count);
        Assert.All(organizationEndpoints, endpoint =>
        {
            var policies = endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Select(metadata => metadata.Policy)
                .Where(policy => policy is not null)
                .ToList();

            Assert.Contains(AuthPolicies.RequireSuperAdmin, policies);
            Assert.DoesNotContain(AuthPolicies.RequireAdmin, policies);
        });
    }
}
