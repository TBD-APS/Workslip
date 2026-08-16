using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Api.Endpoints;
using Workslip.Application.Operations;
using Xunit;

namespace Workslip.Tests.Operations;

public sealed class ControlCenterEndpointAuthorizationTests
{
    [Fact]
    public async Task ControlCenterEndpoints_require_superadmin_policy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IControlCenterReadService, StubControlCenterReadService>();
        await using var app = builder.Build();
        app.MapControlCenterEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(route => route.RoutePattern.RawText?.StartsWith("/api/admin/control-center/", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(2, endpoints.Count);

        foreach (var endpoint in endpoints)
        {
            var policies = endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Select(metadata => metadata.Policy)
                .Where(policy => policy is not null)
                .ToList();

            Assert.Contains(AuthPolicies.RequireSuperAdmin, policies);
            Assert.DoesNotContain(AuthPolicies.RequireAdmin, policies);
        }
    }

    private sealed class StubControlCenterReadService : IControlCenterReadService
    {
        public Task<Result<ControlCenterSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result<ControlCenterSnapshot>.Success(
                new ControlCenterSnapshot(DateTimeOffset.UtcNow, [])));
    }
}
