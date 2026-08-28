using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Workslip.Application.Integrations;

namespace Workslip.Application.Integrations;

public class ShopifyPaymentOptions
{
    public const string SectionName = "ShopifyPayment";
    public string ShopDomain { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2024-01";
    public string WebhookSecret { get; set; } = string.Empty;
}

public class ShopifyPaymentProvider : IPaymentProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ShopifyPaymentOptions _options;

    public ShopifyPaymentProvider(IHttpClientFactory httpClientFactory, IOptions<ShopifyPaymentOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public string ProviderId => "shopify";
    public string DisplayName => "Shopify Payments";

    public async Task<bool> TestConnectionAsync(string tenantId)
    {
        try
        {
            using var client = CreateClient();
            var response = await client.GetAsync($"/admin/api/{_options.ApiVersion}/shop.json");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<PaymentCheckout> CreateCheckoutAsync(
        string tenantId,
        string customerId,
        decimal amount,
        string currency,
        string returnUrl,
        string cancelUrl,
        Dictionary<string, string>? metadata = null)
    {
        using var client = CreateClient();

        var lineItems = new[]
        {
            new
            {
                title = metadata?.GetValueOrDefault("description") ?? "Workslip Payment",
                price = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                quantity = 1,
                properties = metadata?.Where(kvp => kvp.Key != "description").Select(kvp => new { name = kvp.Key, value = kvp.Value }).ToArray() ?? Array.Empty<object>()
            }
        };

        long? parsedCustomerId = null;
        if (!string.IsNullOrEmpty(customerId) && long.TryParse(customerId, out var cid))
        {
            parsedCustomerId = cid;
        }

        var checkoutRequest = new
        {
            checkout = new
            {
                line_items = lineItems,
                customer_id = parsedCustomerId,
                financial_status = "pending",
                currency = currency,
                note = metadata?.GetValueOrDefault("note"),
                attributes = metadata?.Where(kvp => kvp.Key != "description" && kvp.Key != "note").Select(kvp => new { name = kvp.Key, value = kvp.Value }).ToArray() ?? Array.Empty<object>()
            }
        };

        var response = await client.PostAsJsonAsync($"/admin/api/{_options.ApiVersion}/checkouts.json", checkoutRequest);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Shopify checkout creation failed: {response.StatusCode} - {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<ShopifyCheckoutResponse>();
        var checkout = result?.Checkout;

        if (checkout == null)
        {
            throw new InvalidOperationException("Shopify returned empty checkout response");
        }

        return new PaymentCheckout(
            CheckoutId: checkout.Id.ToString(),
            CheckoutUrl: checkout.WebUrl,
            OrderId: checkout.OrderId?.ToString() ?? string.Empty,
            Amount: checkout.TotalPrice,
            Currency: checkout.Currency,
            Status: checkout.FinancialStatus
        );
    }

    public async Task<PaymentResult> GetPaymentStatusAsync(string tenantId, string checkoutId)
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/admin/api/{_options.ApiVersion}/checkouts/{checkoutId}.json");
        if (!response.IsSuccessStatusCode)
        {
            return new PaymentResult(false, string.Empty, string.Empty, "Error", 0, string.Empty);
        }

        var result = await response.Content.ReadFromJsonAsync<ShopifyCheckoutResponse>();
        var checkout = result?.Checkout;

        if (checkout == null)
        {
            return new PaymentResult(false, string.Empty, string.Empty, "NotFound", 0, string.Empty);
        }

        var isPaid = checkout.FinancialStatus == "paid" || checkout.FinancialStatus == "partially_paid";

        return new PaymentResult(
            Success: isPaid,
            OrderId: checkout.OrderId?.ToString() ?? string.Empty,
            TransactionId: checkout.Id.ToString(),
            Status: checkout.FinancialStatus,
            Amount: checkout.TotalPrice,
            Currency: checkout.Currency
        );
    }

    public async Task<bool> HandleWebhookAsync(string tenantId, string payload, string signature)
    {
        if (!VerifyWebhookSignature(payload, signature))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idElement) &&
                root.TryGetProperty("financial_status", out var statusElement))
            {
                var orderId = idElement.GetInt64().ToString();
                var financialStatus = statusElement.GetString() ?? string.Empty;

                // TODO: Update local payment/order status based on webhook
                // This would typically call a domain service to mark payment as completed
                // For now, we just acknowledge the webhook
                return financialStatus == "paid" || financialStatus == "partially_paid";
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri($"https://{_options.ShopDomain}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private bool VerifyWebhookSignature(string payload, string signature)
    {
        if (string.IsNullOrEmpty(_options.WebhookSecret) || string.IsNullOrEmpty(signature))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.WebhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computedSignature = Convert.ToBase64String(hash);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computedSignature), Encoding.UTF8.GetBytes(signature));
    }

    private record ShopifyCheckoutResponse(ShopifyCheckout Checkout);
    private record ShopifyCheckout(
        long Id,
        string WebUrl,
        long? OrderId,
        decimal TotalPrice,
        string Currency,
        string FinancialStatus
    );
}