using System.Collections.Generic;
using System.Threading.Tasks;

namespace Workslip.Application.Integrations;

public record PaymentCheckout(
    string CheckoutId,
    string CheckoutUrl,
    string OrderId,
    decimal Amount,
    string Currency,
    string Status
);

public record PaymentResult(
    bool Success,
    string OrderId,
    string TransactionId,
    string Status,
    decimal Amount,
    string Currency
);

public interface IPaymentProvider : IIntegrationProvider
{
    Task<PaymentCheckout> CreateCheckoutAsync(string tenantId, string customerId, decimal amount, string currency, string returnUrl, string cancelUrl, Dictionary<string, string>? metadata = null);
    Task<PaymentResult> GetPaymentStatusAsync(string tenantId, string checkoutId);
    Task<bool> HandleWebhookAsync(string tenantId, string payload, string signature);
}