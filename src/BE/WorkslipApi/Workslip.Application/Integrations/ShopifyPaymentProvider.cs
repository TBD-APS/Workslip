using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Workslip.Application.Integrations;

public sealed class ShopifyPaymentOptions
{
    public const string SectionName = "ShopifyPayment";

    public bool EnableHostedCheckout { get; set; }
    public string ShopDomain { get; set; } = string.Empty;
    public string StorefrontPrivateAccessToken { get; set; } = string.Empty;
    public string ProductVariantId { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2026-01";
}

public sealed class ShopifyPaymentProvider : IPaymentProvider
{
    private const string StorefrontPrivateTokenHeader = "Shopify-Storefront-Private-Token";
    private const string StorefrontBuyerIpHeader = "Shopify-Storefront-Buyer-IP";
    private const string CartCreateMutation = """
        mutation CartCreate($input: CartInput!) {
          cartCreate(input: $input) {
            cart {
              checkoutUrl
              cost {
                totalAmount {
                  amount
                  currencyCode
                }
              }
            }
            userErrors {
              field
              message
            }
          }
        }
        """;
    private const string ConnectionQuery = """
        query StorefrontConnection {
          shop {
            name
          }
        }
        """;

    private readonly HttpClient _httpClient;
    private readonly ShopifyPaymentOptions _options;

    public ShopifyPaymentProvider(HttpClient httpClient, IOptions<ShopifyPaymentOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string ProviderId => "shopify";
    public string DisplayName => "Shopify Payments";

    public async Task<bool> TestConnectionAsync(string tenantId)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
            using var response = await SendStorefrontRequestAsync(ConnectionQuery, variables: null, buyerIp: null);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var payload = await response.Content.ReadFromJsonAsync<ShopifyGraphQlResponse<ShopifyConnectionData>>();
            return payload?.Data?.Shop?.Name is not null && payload.Errors is not { Count: > 0 };
        }
        catch
        {
            return false;
        }
    }

    public async Task<PaymentCheckout> CreateCheckoutAsync(
        string tenantId,
        int quantity,
        string? buyerIp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quantity, 100);

        var variables = new
        {
            input = new
            {
                lines = new[]
                {
                    new
                    {
                        merchandiseId = _options.ProductVariantId,
                        quantity
                    }
                }
            }
        };

        using var response = await SendStorefrontRequestAsync(CartCreateMutation, variables, buyerIp);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Shopify Storefront API did not create a cart ({(int)response.StatusCode}).");
        }

        var result = await response.Content.ReadFromJsonAsync<ShopifyGraphQlResponse<ShopifyCartCreateData>>();
        var cartCreate = result?.Data?.CartCreate;
        if (result?.Errors is { Count: > 0 } || cartCreate?.UserErrors is { Count: > 0 })
        {
            throw new InvalidOperationException("Shopify Storefront API rejected the hosted checkout request.");
        }

        var cart = cartCreate?.Cart;

        if (cart?.CheckoutUrl is null || cart.Cost?.TotalAmount is null)
        {
            throw new InvalidOperationException("Shopify Storefront API returned an incomplete cart response.");
        }

        if (!decimal.TryParse(cart.Cost.TotalAmount.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            throw new InvalidOperationException("Shopify Storefront API returned an invalid cart amount.");
        }

        return new PaymentCheckout(
            CheckoutUrl: cart.CheckoutUrl,
            Amount: amount,
            Currency: cart.Cost.TotalAmount.CurrencyCode
        );
    }

    private async Task<HttpResponseMessage> SendStorefrontRequestAsync(
        string query,
        object? variables,
        string? buyerIp)
    {
        EnsureConfiguration();

        using var request = new HttpRequestMessage(HttpMethod.Post, GetStorefrontApiUri())
        {
            Content = JsonContent.Create(new { query, variables })
        };
        request.Headers.Add(StorefrontPrivateTokenHeader, _options.StorefrontPrivateAccessToken);

        if (!string.IsNullOrWhiteSpace(buyerIp) && IPAddress.TryParse(buyerIp, out _))
        {
            request.Headers.Add(StorefrontBuyerIpHeader, buyerIp);
        }

        return await _httpClient.SendAsync(request);
    }

    private void EnsureConfiguration()
    {
        if (!_options.EnableHostedCheckout)
        {
            throw new PaymentProviderConfigurationException("Shopify hosted checkout is disabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.ShopDomain) ||
            string.IsNullOrWhiteSpace(_options.StorefrontPrivateAccessToken) ||
            string.IsNullOrWhiteSpace(_options.ProductVariantId))
        {
            throw new PaymentProviderConfigurationException("Shopify hosted checkout is not configured.");
        }

        if (Uri.CheckHostName(_options.ShopDomain.Trim()) != UriHostNameType.Dns ||
            !_options.ShopDomain.Trim().EndsWith(".myshopify.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentProviderConfigurationException("Shopify shop domain must be a .myshopify.com host.");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(_options.ApiVersion, "^\\d{4}-\\d{2}$") ||
            !_options.ProductVariantId.StartsWith("gid://shopify/ProductVariant/", StringComparison.Ordinal))
        {
            throw new PaymentProviderConfigurationException("Shopify hosted checkout configuration is invalid.");
        }
    }

    private Uri GetStorefrontApiUri()
    {
        return new Uri($"https://{_options.ShopDomain.Trim().TrimEnd('/')}/api/{_options.ApiVersion}/graphql.json");
    }

    private sealed record ShopifyGraphQlResponse<T>(T? Data, IReadOnlyList<ShopifyGraphQlError>? Errors);
    private sealed record ShopifyGraphQlError(string Message);
    private sealed record ShopifyConnectionData(ShopifyShop? Shop);
    private sealed record ShopifyShop(string? Name);
    private sealed record ShopifyCartCreateData(ShopifyCartCreate? CartCreate);
    private sealed record ShopifyCartCreate(ShopifyCart? Cart, IReadOnlyList<ShopifyUserError>? UserErrors);
    private sealed record ShopifyUserError(IReadOnlyList<string>? Field, string Message);
    private sealed record ShopifyCart(string? CheckoutUrl, ShopifyCartCost? Cost);
    private sealed record ShopifyCartCost(ShopifyMoney? TotalAmount);
    private sealed record ShopifyMoney(string Amount, string CurrencyCode);
}
