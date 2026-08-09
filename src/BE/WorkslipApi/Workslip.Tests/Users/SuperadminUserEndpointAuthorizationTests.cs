using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Api.Endpoints;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Xunit;

namespace Workslip.Tests.Users;

public sealed class SuperadminUserEndpointAuthorizationTests
{
    [Fact]
    public async Task SuperadminUserEndpoints_RequireSuperAdminPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ISuperadminUserService, StubSuperadminUserService>();
        await using var app = builder.Build();
        app.MapSuperadminUserEndpoints();

        var dataSources = ((IEndpointRouteBuilder)app).DataSources;
        var endpoints = dataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/superadmin/users", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(5, endpoints.Count);
        Assert.All(endpoints, endpoint =>
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

    private sealed class StubSuperadminUserService : ISuperadminUserService
    {
        public Task<Result<AdminUserListResponse>> ListAsync(
            Guid? organizationId,
            int? limit,
            int? offset,
            string? search,
            string? sortBy,
            string? sortDirection,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<AdminUserListResponse>.Success(new AdminUserListResponse([], 0)));

        public Task<Result<AdminUserResponse>> GetAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Result<AdminUserResponse>.NotFound());

        public Task<Result<AdminUserResponse>> CreateAsync(CreateAdminUserRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<AdminUserResponse>.NotFound());

        public Task<Result<AdminUserResponse>> UpdateAsync(Guid userId, UpdateAdminUserRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<AdminUserResponse>.NotFound());

        public Task<Result> DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.NotFound());
    }
}
