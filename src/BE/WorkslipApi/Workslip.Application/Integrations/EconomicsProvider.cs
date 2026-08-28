using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.IO;

namespace Workslip.Application.Integrations;

public class EconomicsProvider : IAccountingProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private const string BaseUrl = "https://restapi.e-conomic.com";

    public EconomicsProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string ProviderId => "economics";
    public string DisplayName => "e-conomic";

    public async Task<bool> TestConnectionAsync(string tenantId)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-AgreementGrantToken", "demo");
            client.DefaultRequestHeaders.Add("X-AppSecretToken", "demo");

            var response = await client.GetAsync($"{BaseUrl}/customers");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IEnumerable<AccountingDocument>> GetDocumentsForUserAsync(string tenantId, string userId, string startDate, string endDate)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-AgreementGrantToken", "demo");
            client.DefaultRequestHeaders.Add("X-AppSecretToken", "demo");

            // e-conomic demo: /invoices returns links, actual data is under /invoices/booked
            var response = await client.GetAsync($"{BaseUrl}/invoices/booked");
            if (!response.IsSuccessStatusCode) return Enumerable.Empty<AccountingDocument>();

            var wrapper = await response.Content.ReadFromJsonAsync<EconomicsBookedCollection>();
            if (wrapper?.Collection == null) return Enumerable.Empty<AccountingDocument>();

            return wrapper.Collection.Select(inv => new AccountingDocument(
                DocumentId: inv.BookedInvoiceNumber.ToString(),
                DocumentNumber: $"FAK-{inv.BookedInvoiceNumber:D4}",
                Type: "Invoice",
                Amount: inv.NetAmount,
                Date: inv.Date,
                Status: inv.Remainder == 0 ? "Paid" : "Unpaid",
                ExternalLink: $"{BaseUrl}/invoices/booked/{inv.BookedInvoiceNumber}"
            ));
        }
        catch
        {
            return Enumerable.Empty<AccountingDocument>();
        }
    }

    public async Task<Stream?> GetDocumentStreamAsync(string tenantId, string documentId)
    {
        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AgreementGrantToken", "demo");
        client.DefaultRequestHeaders.Add("X-AppSecretToken", "demo");

        var response = await client.GetAsync($"{BaseUrl}/invoices/{documentId}/pdf");
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadAsStreamAsync();
    }

    public async Task<bool> SyncHoursAsync(string tenantId, object hoursData)
    {
        // Demo only supports GET
        return false;
    }

    private record EconomicsBookedCollection(List<EconomicsBookedInvoice> Collection);
    private record EconomicsBookedInvoice(
        int BookedInvoiceNumber,
        int OrderNumber,
        string Date,
        string Currency,
        decimal NetAmount,
        decimal GrossAmount,
        decimal VatAmount,
        decimal Remainder);
}
