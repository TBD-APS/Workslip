using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Api.Endpoints;
using Workslip.Application.Organizations;
using Xunit;

namespace Workslip.Tests.Organizations;

public sealed class OrganizationEndpointAuthorizationTests
{
    [Fact]
    public async Task OrganizationAdministrationEndpoints_RequireSuperAdminPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IOrganizationService, StubOrganizationService>();
        await using var app = builder.Build();
        app.MapOrganizationEndpoints();

        var dataSources = ((IEndpointRouteBuilder)app).DataSources;
        var organizationEndpoints = dataSources
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

    private sealed class StubOrganizationService : IOrganizationService
    {
        public Task<Result<OrganizationOnboardingResponse>> CreateAsync(
            CreateOrganizationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<OrganizationOnboardingResponse>.NotFound());

        public Task<Result<OrganizationUserResponse>> UpsertAdminAsync(
            Guid organizationId,
            UpsertOrganizationAdminRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<OrganizationUserResponse>.NotFound());
    }
}
