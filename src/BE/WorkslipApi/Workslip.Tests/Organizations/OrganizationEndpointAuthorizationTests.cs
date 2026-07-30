using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Api.Endpoints;
using Workslip.Application.Auth;
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
        builder.Services.AddSingleton<IOrganizationSessionService, StubOrganizationSessionService>();
        await using var app = builder.Build();
        app.MapOrganizationEndpoints();

        var dataSources = ((IEndpointRouteBuilder)app).DataSources;
        var organizationEndpoints = dataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/organizations", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(4, organizationEndpoints.Count);
        Assert.Contains(
            organizationEndpoints,
            endpoint => endpoint.RoutePattern.RawText == "/api/organizations/{organizationId:guid}/session");
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
        public Task<Result<IReadOnlyList<OrganizationResponse>>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<OrganizationResponse>>.Success([]));

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

    private sealed class StubOrganizationSessionService : IOrganizationSessionService
    {
        public Task<Result<OrganizationSessionContext>> CreateAsync(
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<OrganizationSessionContext>.NotFound());
    }
}
