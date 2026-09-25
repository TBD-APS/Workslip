using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Api.Endpoints;
using Workslip.Application.Auth;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Analytics;

public sealed class WorkflowStatisticsEndpointAuthorizationTests
{
    [Fact]
    public async Task WorkflowStatisticsEndpoint_RequiresSuperAdminPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ICurrentUserContext, StubCurrentUserContext>();
        builder.Services.AddSingleton(_ =>
            new SqlDbContext(new DbContextOptionsBuilder<SqlDbContext>().Options));

        await using var app = builder.Build();
        app.MapWorkflowStatisticsEndpoints();

        var endpoint = FindEndpoint(app, "/api/superadmin/analytics/workflow-statistics");
        var policies = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(metadata => metadata.Policy)
            .Where(policy => policy is not null)
            .ToList();

        Assert.Contains(AuthPolicies.RequireSuperAdmin, policies);
        Assert.DoesNotContain(AuthPolicies.RequireAdmin, policies);
    }

    [Fact]
    public async Task ActiveSegmentEndpoint_UsesAuthenticatedUserBoundary_NotSuperAdminBoundary()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ICurrentUserContext, StubCurrentUserContext>();
        builder.Services.AddSingleton(_ =>
            new SqlDbContext(new DbContextOptionsBuilder<SqlDbContext>().Options));

        await using var app = builder.Build();
        app.MapWorkflowStatisticsEndpoints();

        var endpoint = FindEndpoint(app, "/api/productivity/workflow-active-segment");
        var policies = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(metadata => metadata.Policy)
            .Where(policy => policy is not null)
            .ToList();

        Assert.Contains(AuthPolicies.RequireUser, policies);
        Assert.DoesNotContain(AuthPolicies.RequireSuperAdmin, policies);
    }

    private static RouteEndpoint FindEndpoint(WebApplication app, string route) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == route);

    private sealed class StubCurrentUserContext : ICurrentUserContext
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? OrganizationId => Guid.NewGuid();
        public string? Role => Roles.Superadmin;
    }
}