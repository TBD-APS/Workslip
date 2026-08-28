using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Workslip.Api.Endpoints;
using Workslip.Application.Auth;
using Xunit;

namespace Workslip.Tests.Integrations;

public sealed class PaymentEndpointsAuthorizationTests
{
    [Fact]
    public async Task HostedCheckout_requires_an_authenticated_user_and_exposes_no_webhook_or_cart_identifier_route()
    {
        var builder = WebApplication.CreateBuilder();
        await using var app = builder.Build();
        app.MapPaymentEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/payments", StringComparison.Ordinal) == true)
            .ToList();

        var checkout = Assert.Single(endpoints);
        Assert.Equal("/api/payments/checkout", checkout.RoutePattern.RawText);
        var policies = checkout.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(metadata => metadata.Policy)
            .Where(policy => policy is not null)
            .ToList();

        Assert.Contains(AuthPolicies.RequireUser, policies);
    }
}
