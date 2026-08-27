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
        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AgreementGrantToken", "demo");
        client.DefaultRequestHeaders.Add("X-AppSecretToken", "demo");

        // Note: In a real scenario, we would filter by userId and date.
        // For the demo, we fetch invoices and map them.
        var response = await client.GetAsync($"{BaseUrl}/invoices");
        if (!response.IsSuccessStatusCode) return Enumerable.Empty<AccountingDocument>();

        var data = await response.Content.ReadFromJsonAsync<List<EconomicsInvoice>>();
        if (data == null) return Enumerable.Empty<AccountingDocument>();

        return data.Select(inv => new AccountingDocument(
            DocumentId: inv.Id.ToString(),
            DocumentNumber: inv.Number,
            Type: "Invoice",
            Amount: inv.Amount,
            Date: inv.Date,
            Status: "Demo",
            ExternalLink: $"{BaseUrl}/invoices/{inv.Id}"
        ));
    }

    public async Task<Stream> GetDocumentStreamAsync(string tenantId, string documentId)
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

    private record EconomicsInvoice(
        Guid Id,
        string Number,
        decimal Amount,
        string Date);
}
