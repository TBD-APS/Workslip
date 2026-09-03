using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Workslip.Application.Integrations;

public sealed class EconomicsProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IAccountingOperationsProvider
{
    private const string BaseUrl = "https://restapi.e-conomic.com/";

    public string ProviderId => "economics";
    public string DisplayName => "e-conomic";

    public bool IsConfigured(string tenantId) =>
        !string.IsNullOrWhiteSpace(configuration["Integrations:Economic:AppSecretToken"]) &&
        !string.IsNullOrWhiteSpace(configuration[$"Integrations:Economic:Agreements:{tenantId}:GrantToken"]);

    public async Task<bool> TestConnectionAsync(string tenantId)
    {
        if (!IsConfigured(tenantId)) return false;
        try
        {
            using var client = CreateClient(tenantId);
            using var response = await client.GetAsync("customers?pagesize=1");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ExternalAccountingCustomer>> GetCustomersAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(tenantId);
        var rows = await GetCollectionAsync(client, "customers?pagesize=1000", cancellationToken);
        return rows.Select(ToExternalCustomer).ToArray();
    }

    public async Task<ExternalAccountingCustomer> UpsertCustomerAsync(
        string tenantId,
        ExternalAccountingCustomer customer,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(tenantId);
        JsonObject payload;
        HttpMethod method;
        string path;

        if (string.IsNullOrWhiteSpace(customer.ExternalCustomerNumber))
        {
            payload = CreateCustomerPayload(customer);
            method = HttpMethod.Post;
            path = "customers";
        }
        else
        {
            path = $"customers/{Uri.EscapeDataString(customer.ExternalCustomerNumber)}";
            using var currentResponse = await client.GetAsync(path, cancellationToken);
            currentResponse.EnsureSuccessStatusCode();
            payload = await ParseObjectAsync(currentResponse, cancellationToken);
            ApplyCustomerFields(payload, customer);
            method = HttpMethod.Put;
        }

        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(payload)
        };
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var saved = await ParseObjectAsync(response, cancellationToken);
        return ToExternalCustomer(saved);
    }

    public async Task<AccountingInvoiceState> CreateDraftInvoiceAsync(
        string tenantId,
        AccountingDraftInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Lines.Count == 0)
            throw new InvalidOperationException("Cannot create an empty invoice draft.");

        using var client = CreateClient(tenantId);
        var customerNumber = Uri.EscapeDataString(request.ExternalCustomerNumber);

        using var templateResponse = await client.GetAsync(
            $"customers/{customerNumber}/templates/invoice",
            cancellationToken);
        templateResponse.EnsureSuccessStatusCode();
        var invoice = await ParseObjectAsync(templateResponse, cancellationToken);

        invoice["date"] = request.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        invoice["currency"] = request.Currency;
        invoice["notes"] = new JsonObject
        {
            ["heading"] = request.Heading,
            ["textLine1"] = "Oprettet fra Workslip. Kontroller før bogføring/afsendelse."
        };

        var references = invoice["references"] as JsonObject ?? new JsonObject();
        references["other"] = request.ExternalReference;
        invoice["references"] = references;

        var lines = new JsonArray();
        foreach (var sourceLine in request.Lines)
        {
            var productNumber = ProductNumber(sourceLine.Kind);
            var quantity = sourceLine.Quantity.ToString(CultureInfo.InvariantCulture);
            using var lineResponse = await client.GetAsync(
                $"customers/{customerNumber}/templates/invoiceline/{Uri.EscapeDataString(productNumber)}?quantity={Uri.EscapeDataString(quantity)}",
                cancellationToken);
            lineResponse.EnsureSuccessStatusCode();
            var line = await ParseObjectAsync(lineResponse, cancellationToken);
            line["description"] = sourceLine.Description;
            line["quantity"] = sourceLine.Quantity;
            line["unitNetPrice"] = sourceLine.UnitNetPrice;
            lines.Add(line);
        }
        invoice["lines"] = lines;

        using var post = await client.PostAsJsonAsync("invoices/drafts", invoice, cancellationToken);
        post.EnsureSuccessStatusCode();
        var created = await ParseObjectAsync(post, cancellationToken);
        return ToDraftState(created, request.ExternalReference);
    }

    public async Task<AccountingInvoiceState?> FindInvoiceByReferenceAsync(
        string tenantId,
        string externalReference,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(tenantId);
        var filter = Uri.EscapeDataString($"references.other$eq:{externalReference}");

        var drafts = await GetCollectionAsync(client, $"invoices/drafts?filter={filter}&pagesize=5", cancellationToken);
        var draft = drafts.FirstOrDefault(row =>
            string.Equals(row["references"]?["other"]?.GetValue<string>(), externalReference, StringComparison.Ordinal));
        if (draft is not null)
            return ToDraftState(draft, externalReference);

        var booked = await GetCollectionAsync(client, $"invoices/booked?filter={filter}&pagesize=5", cancellationToken);
        var bookedInvoice = booked.FirstOrDefault(row =>
            string.Equals(row["references"]?["other"]?.GetValue<string>(), externalReference, StringComparison.Ordinal));
        return bookedInvoice is null ? null : ToBookedState(bookedInvoice, externalReference);
    }

    public async Task<IEnumerable<AccountingDocument>> GetDocumentsForUserAsync(
        string tenantId,
        string userId,
        string startDate,
        string endDate)
    {
        _ = userId;
        if (!IsConfigured(tenantId)) return Array.Empty<AccountingDocument>();

        using var client = CreateClient(tenantId);
        var rows = await GetCollectionAsync(client, "invoices/booked?pagesize=1000", CancellationToken.None);
        DateOnly.TryParse(startDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from);
        DateOnly.TryParse(endDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to);

        return rows
            .Where(row =>
            {
                if (!DateOnly.TryParse(row["date"]?.GetValue<string>(), out var date)) return true;
                if (from != default && date < from) return false;
                if (to != default && date > to) return false;
                return true;
            })
            .Select(row =>
            {
                var number = row["bookedInvoiceNumber"]?.GetValue<int>() ?? 0;
                var remainder = row["remainder"]?.GetValue<decimal>() ?? 0m;
                return new AccountingDocument(
                    number.ToString(CultureInfo.InvariantCulture),
                    $"FAK-{number:D4}",
                    "Invoice",
                    row["netAmount"]?.GetValue<decimal>() ?? 0m,
                    row["date"]?.GetValue<string>() ?? string.Empty,
                    remainder == 0m ? "Paid" : "Unpaid",
                    $"{BaseUrl}invoices/booked/{number}");
            })
            .ToArray();
    }

    public async Task<Stream?> GetDocumentStreamAsync(string tenantId, string documentId)
    {
        if (!IsConfigured(tenantId)) return null;
        using var client = CreateClient(tenantId);
        using var response = await client.GetAsync($"invoices/booked/{Uri.EscapeDataString(documentId)}/pdf");
        if (!response.IsSuccessStatusCode) return null;
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return new MemoryStream(bytes, writable: false);
    }

    [Obsolete("Use operational invoice draft synchronization instead of pushing raw hours.")]
    public Task<bool> SyncHoursAsync(string tenantId, object hoursData)
    {
        _ = tenantId;
        _ = hoursData;
        return Task.FromResult(false);
    }

    private HttpClient CreateClient(string tenantId)
    {
        var appSecret = configuration["Integrations:Economic:AppSecretToken"];
        var grantToken = configuration[$"Integrations:Economic:Agreements:{tenantId}:GrantToken"];
        if (string.IsNullOrWhiteSpace(appSecret) || string.IsNullOrWhiteSpace(grantToken))
            throw new InvalidOperationException("e-conomic credentials are not configured for this organization.");

        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(BaseUrl);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-AppSecretToken", appSecret);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-AgreementGrantToken", grantToken);
        return client;
    }

    private JsonObject CreateCustomerPayload(ExternalAccountingCustomer customer)
    {
        var group = RequiredInt("Integrations:Economic:Defaults:CustomerGroupNumber");
        var paymentTerms = RequiredInt("Integrations:Economic:Defaults:PaymentTermsNumber");
        var vatZone = RequiredInt("Integrations:Economic:Defaults:VatZoneNumber");

        var payload = new JsonObject
        {
            ["currency"] = configuration["Integrations:Economic:Defaults:Currency"] ?? "DKK",
            ["customerGroup"] = new JsonObject { ["customerGroupNumber"] = group },
            ["paymentTerms"] = new JsonObject { ["paymentTermsNumber"] = paymentTerms },
            ["vatZone"] = new JsonObject { ["vatZoneNumber"] = vatZone }
        };
        ApplyCustomerFields(payload, customer);
        return payload;
    }

    private static void ApplyCustomerFields(JsonObject payload, ExternalAccountingCustomer customer)
    {
        payload["name"] = customer.Name;
        SetOptional(payload, "address", customer.Address);
        SetOptional(payload, "zip", customer.ZipCode);
        SetOptional(payload, "city", customer.City);
        SetOptional(payload, "country", customer.Country);
        SetOptional(payload, "email", customer.Email);
        SetOptional(payload, "telephoneAndFaxNumber", customer.Phone);
    }

    private static void SetOptional(JsonObject payload, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) payload.Remove(name);
        else payload[name] = value.Trim();
    }

    private ExternalAccountingCustomer ToExternalCustomer(JsonObject row)
    {
        var number = row["customerNumber"]?.ToString() ?? string.Empty;
        return new ExternalAccountingCustomer(
            number,
            row["name"]?.GetValue<string>() ?? $"Kunde {number}",
            StringValue(row, "address"),
            StringValue(row, "zip"),
            StringValue(row, "city"),
            StringValue(row, "country"),
            StringValue(row, "email"),
            null,
            StringValue(row, "telephoneAndFaxNumber") ?? StringValue(row, "mobilePhone"));
    }

    private AccountingInvoiceState ToDraftState(JsonObject row, string reference)
    {
        var number = row["draftInvoiceNumber"]?.GetValue<int>();
        return new AccountingInvoiceState(
            number,
            null,
            "Draft",
            reference,
            number is null ? null : $"{BaseUrl}invoices/drafts/{number}",
            row["netAmount"]?.GetValue<decimal>() ?? CalculateNet(row),
            null,
            ParseDate(row["dueDate"]));
    }

    private AccountingInvoiceState ToBookedState(JsonObject row, string reference)
    {
        var number = row["bookedInvoiceNumber"]?.GetValue<int>();
        var remainder = row["remainder"]?.GetValue<decimal>();
        var dueDate = ParseDate(row["dueDate"]);
        var status = remainder == 0m
            ? "Paid"
            : dueDate is not null && dueDate.Value < DateOnly.FromDateTime(DateTime.UtcNow)
                ? "Overdue"
                : "Booked";
        return new AccountingInvoiceState(
            null,
            number,
            status,
            reference,
            number is null ? null : $"{BaseUrl}invoices/booked/{number}",
            row["netAmount"]?.GetValue<decimal>() ?? CalculateNet(row),
            remainder,
            dueDate);
    }

    private static decimal CalculateNet(JsonObject row)
    {
        if (row["lines"] is not JsonArray lines) return 0m;
        return lines.OfType<JsonObject>().Sum(line =>
            (line["quantity"]?.GetValue<decimal>() ?? 0m) *
            (line["unitNetPrice"]?.GetValue<decimal>() ?? 0m) *
            (1m - ((line["discountPercentage"]?.GetValue<decimal>() ?? 0m) / 100m)));
    }

    private async Task<IReadOnlyList<JsonObject>> GetCollectionAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        var result = new List<JsonObject>();
        string? next = path;
        var pageGuard = 0;

        while (!string.IsNullOrWhiteSpace(next) && pageGuard++ < 100)
        {
            using var response = await client.GetAsync(next, cancellationToken);
            response.EnsureSuccessStatusCode();
            var root = await ParseObjectAsync(response, cancellationToken);
            if (root["collection"] is JsonArray collection)
            {
                result.AddRange(collection
                    .OfType<JsonObject>()
                    .Select(item => (JsonObject)item.DeepClone()));
            }
            next = root["pagination"]?["nextPage"]?.GetValue<string>();
        }

        return result;
    }

    private static async Task<JsonObject> ParseObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("e-conomic returned an unexpected JSON payload.");
    }

    private int RequiredInt(string key) =>
        int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new InvalidOperationException($"Missing or invalid e-conomic configuration '{key}'.");

    private string ProductNumber(string kind)
    {
        var suffix = kind switch
        {
            "hours" => "Hours",
            "material" => "Material",
            "outlay" => "Outlay",
            _ => throw new InvalidOperationException($"Unsupported invoice line kind '{kind}'.")
        };
        return configuration[$"Integrations:Economic:Products:{suffix}"]
            ?? throw new InvalidOperationException($"Missing e-conomic product mapping for '{kind}'.");
    }

    private static string? StringValue(JsonObject row, string property) =>
        row[property] is null ? null : row[property]!.GetValue<string>();

    private static DateOnly? ParseDate(JsonNode? node) =>
        node is not null && DateOnly.TryParse(node.GetValue<string>(), out var value) ? value : null;
}
