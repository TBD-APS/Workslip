using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Api.Endpoints;
using Workslip.Application.Diagnostics;
using Xunit;

namespace Workslip.Tests.Diagnostics;

public sealed class DiagnosticsEndpointAuthorizationTests
{
    [Fact]
    public async Task ErrorDiagnosticsEndpoint_RequiresSuperAdminPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IErrorDiagnosticsService, StubErrorDiagnosticsService>();
        await using var app = builder.Build();
        app.MapDiagnosticsEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(route => route.RoutePattern.RawText == "/api/admin/diagnostics/errors");

        var policies = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(metadata => metadata.Policy)
            .Where(policy => policy is not null)
            .ToList();

        Assert.Contains(AuthPolicies.RequireSuperAdmin, policies);
        Assert.DoesNotContain(AuthPolicies.RequireAdmin, policies);
    }

    private sealed class StubErrorDiagnosticsService : IErrorDiagnosticsService
    {
        public Task<Result<ErrorDiagnosticsDashboard>> GetAsync(
            ErrorDiagnosticsQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ErrorDiagnosticsDashboard>.Success(
                ErrorDiagnosticsDashboard.Unavailable("not_configured")));
    }
}
