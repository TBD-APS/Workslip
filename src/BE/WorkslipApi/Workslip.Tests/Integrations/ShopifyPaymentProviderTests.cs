using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Workslip.Application.Integrations;
using Xunit;

namespace Workslip.Tests.Integrations;

public sealed class ShopifyPaymentProviderTests
{
    [Fact]
    public async Task CreateCheckoutAsync_uses_private_storefront_api_and_returns_hosted_checkout()
    {
        var handler = new CapturingHandler(
            """
            {
              "data": {
                "cartCreate": {
                  "cart": {
                    "checkoutUrl": "https://example.myshopify.com/checkouts/cart-token",
                    "cost": {
                      "totalAmount": { "amount": "299.00", "currencyCode": "DKK" }
                    }
                  },
                  "userErrors": []
                }
              }
            }
            """);
        var provider = CreateProvider(handler);

        var result = await provider.CreateCheckoutAsync(
            "11111111-1111-1111-1111-111111111111",
            quantity: 2,
            buyerIp: "203.0.113.8");

        Assert.Equal("https://example.myshopify.com/checkouts/cart-token", result.CheckoutUrl);
        Assert.Equal(299m, result.Amount);
        Assert.Equal("DKK", result.Currency);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://example.myshopify.com/api/2026-01/graphql.json", request.Url);
        Assert.Equal("private-token", request.Headers["Shopify-Storefront-Private-Token"]);
        Assert.Equal("203.0.113.8", request.Headers["Shopify-Storefront-Buyer-IP"]);

        using var body = JsonDocument.Parse(request.Body);
        var input = body.RootElement.GetProperty("variables").GetProperty("input");
        var line = Assert.Single(input.GetProperty("lines").EnumerateArray());
        Assert.Equal("gid://shopify/ProductVariant/123", line.GetProperty("merchandiseId").GetString());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
        Assert.Contains("cartCreate", body.RootElement.GetProperty("query").GetString());
    }

    [Fact]
    public async Task CreateCheckoutAsync_fails_closed_when_hosted_checkout_is_disabled()
    {
        var handler = new CapturingHandler("{}");
        var provider = new ShopifyPaymentProvider(
            new HttpClient(handler),
            Options.Create(new ShopifyPaymentOptions()));

        await Assert.ThrowsAsync<PaymentProviderConfigurationException>(() =>
            provider.CreateCheckoutAsync(
                "11111111-1111-1111-1111-111111111111",
                quantity: 1,
                buyerIp: "203.0.113.8"));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateCheckoutAsync_does_not_expose_storefront_user_errors()
    {
        var handler = new CapturingHandler(
            """
            {
              "data": {
                "cartCreate": {
                  "cart": null,
                  "userErrors": [
                    { "field": ["lines"], "message": "Internal catalog detail" }
                  ]
                }
              }
            }
            """);
        var provider = CreateProvider(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CreateCheckoutAsync(
                "11111111-1111-1111-1111-111111111111",
                quantity: 1,
                buyerIp: null));

        Assert.Equal("Shopify Storefront API rejected the hosted checkout request.", exception.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_uses_the_storefront_graphql_endpoint()
    {
        var handler = new CapturingHandler("""{ "data": { "shop": { "name": "Example" } } }""");
        var provider = CreateProvider(handler);

        var connected = await provider.TestConnectionAsync("11111111-1111-1111-1111-111111111111");

        Assert.True(connected);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://example.myshopify.com/api/2026-01/graphql.json", request.Url);
        Assert.Equal("private-token", request.Headers["Shopify-Storefront-Private-Token"]);
        Assert.DoesNotContain("Shopify-Storefront-Buyer-IP", request.Headers.Keys);
        Assert.Contains("StorefrontConnection", request.Body);
    }

    private static ShopifyPaymentProvider CreateProvider(CapturingHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new ShopifyPaymentOptions
            {
                EnableHostedCheckout = true,
                ShopDomain = "example.myshopify.com",
                StorefrontPrivateAccessToken = "private-token",
                ProductVariantId = "gid://shopify/ProductVariant/123",
                ApiVersion = "2026-01"
            }));

    private sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(
                pair => pair.Key,
                pair => string.Join(",", pair.Value),
                StringComparer.OrdinalIgnoreCase);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                headers,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Url,
        IReadOnlyDictionary<string, string> Headers,
        string Body);
}
