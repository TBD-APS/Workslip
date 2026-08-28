using System.Threading.Tasks;

namespace Workslip.Application.Integrations;

public record PaymentCheckout(
    string CheckoutUrl,
    decimal Amount,
    string Currency
);

public interface IPaymentProvider : IIntegrationProvider
{
    Task<PaymentCheckout> CreateCheckoutAsync(string tenantId, int quantity, string? buyerIp);
}

public sealed class PaymentProviderConfigurationException(string message) : InvalidOperationException(message);
