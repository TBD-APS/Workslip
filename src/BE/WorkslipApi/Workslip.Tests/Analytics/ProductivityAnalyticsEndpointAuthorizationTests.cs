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

public sealed class ProductivityAnalyticsEndpointAuthorizationTests
{
    [Fact]
    public async Task ActivationScoreboardEndpoint_RequiresSuperAdminPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ICurrentUserContext, StubCurrentUserContext>();
        builder.Services.AddSingleton(_ =>
            new SqlDbContext(new DbContextOptionsBuilder<SqlDbContext>().Options));

        await using var app = builder.Build();
        app.MapProductivityAnalyticsEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(route => route.RoutePattern.RawText == "/api/superadmin/analytics/activation-scoreboard");

        var policies = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(metadata => metadata.Policy)
            .Where(policy => policy is not null)
            .ToList();

        Assert.Contains(AuthPolicies.RequireSuperAdmin, policies);
        Assert.DoesNotContain(AuthPolicies.RequireAdmin, policies);
    }

    private sealed class StubCurrentUserContext : ICurrentUserContext
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? OrganizationId => Guid.NewGuid();
        public string? Role => "Superadmin";
    }
}
