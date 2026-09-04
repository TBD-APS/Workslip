using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Workslip.Application.Integrations;

public sealed class EconomicsProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IEconomicConnectionStore? connectionStore = null) : IAccountingOperationsProvider, IEconomicConnectionVerifier
{
    private const string BaseUrl = "https://restapi.e-conomic.com/";

    public string ProviderId => "economics";
    public string DisplayName => "e-conomic";

    public bool IsConfigured(string tenantId) =>
        !string.IsNullOrWhiteSpace(configuration["Integrations:Economic:AppSecretToken"]) &&
        (!string.IsNullOrWhiteSpace(configuration[$"Integrations:Economic:Agreements:{tenantId}:GrantToken"]) || connectionStore is not null);

    public async Task<bool> TestConnectionAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(configuration["Integrations:Economic:AppSecretToken"])) return false;
        try
        {
            using var client = await CreateClientAsync(tenantId, CancellationToken.None);
            using var response = await client.GetAsync("self");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<EconomicAgreementIdentity> VerifyGrantTokenAsync(
        string agreementGrantToken,
        CancellationToken cancellationToken)
    {
        using var client = CreateClientWithGrantToken(agreementGrantToken);
        using var response = await client.GetAsync("self", cancellationToken);
        response.EnsureSuccessStatusCode();
        var self = await ParseObjectAsync(response, cancellationToken);

        return new EconomicAgreementIdentity(
            AgreementNumber: NodeText(self["agreementNumber"]) ?? NodeText(self["agreement"]?["agreementNumber"]),
            CompanyName: NodeText(self["company"]?["name"]) ?? NodeText(self["name"]));
    }

    public async Task<IReadOnlyList<ExternalAccountingCustomer>> GetCustomersAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(tenantId, cancellationToken);
        var rows = await GetCollectionAsync(client, "customers?pagesize=1000", cancellationToken);
        return rows.Select(ToExternalCustomer).ToArray();
    }

    public async Task<ExternalAccountingCustomer> UpsertCustomerAsync(
        string tenantId,
        ExternalAccountingCustomer customer,
        CancellationToken cancellationToken)
    {
        using var client = await CreateClientAsync(tenantId, cancellationToken);
        JsonObject payload;
        HttpMethod method;
        string path;

        if (string.IsNullOrWhiteSpace(customer.ExternalCustomerNumber))
        {
            payload = await CreateCustomerPayloadAsync(client, customer, cancellationToken);
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

        using var client = await CreateClientAsync(tenantId, cancellationToken);
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
            var productNumber = ProductNumberOrNull(sourceLine.Kind);
            JsonObject line;

            if (!string.IsNullOrWhiteSpace(productNumber))
            {
                var quantity = sourceLine.Quantity.ToString(CultureInfo.InvariantCulture);
                using var lineResponse = await client.GetAsync(
                    $"customers/{customerNumber}/templates/invoiceline/{Uri.EscapeDataString(productNumber)}?quantity={Uri.EscapeDataString(quantity)}",
                    cancellationToken);
                lineResponse.EnsureSuccessStatusCode();
                line = await ParseObjectAsync(lineResponse, cancellationToken);
            }
            else
            {
                line = new JsonObject { ["discountPercentage"] = 0m };
            }

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
        using var client = await CreateClientAsync(tenantId, cancellationToken);
        var filter = Uri.EscapeDataString($"references.other$eq:{externalReference}");

        var drafts = await GetCollectionAsync(client, $"invoices/drafts?filter={filter}&pagesize=5", cancellationToken);
        var draft = drafts.FirstOrDefault(row =>
            string.Equals(NodeText(row["references"]?["other"]), externalReference, StringComparison.Ordinal));
        if (draft is not null)
            return ToDraftState(draft, externalReference);

        var booked = await GetCollectionAsync(client, $"invoices/booked?filter={filter}&pagesize=5", cancellationToken);
        var bookedInvoice = booked.FirstOrDefault(row =>
            string.Equals(NodeText(row["references"]?["other"]), externalReference, StringComparison.Ordinal));
        return bookedInvoice is null ? null : ToBookedState(bookedInvoice, externalReference);
    }

    public async Task<IEnumerable<AccountingDocument>> GetDocumentsForUserAsync(
        string tenantId,
        string userId,
        string startDate,
        string endDate)
    {
        _ = userId;
        if (string.IsNullOrWhiteSpace(configuration["Integrations:Economic:AppSecretToken"]))
            return Array.Empty<AccountingDocument>();

        using var client = await CreateClientAsync(tenantId, CancellationToken.None);
        var rows = await GetCollectionAsync(client, "invoices/booked?pagesize=1000", CancellationToken.None);
        DateOnly.TryParse(startDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from);
        DateOnly.TryParse(endDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to);

        return rows
            .Where(row =>
            {
                if (!DateOnly.TryParse(NodeText(row["date"]), out var date)) return true;
                if (from != default && date < from) return false;
                if (to != default && date > to) return false;
                return true;
            })
            .Select(row =>
            {
                var number = NodeInt(row["bookedInvoiceNumber"]) ?? 0;
                var remainder = NodeDecimal(row["remainder"]) ?? 0m;
                return new AccountingDocument(
                    number.ToString(CultureInfo.InvariantCulture),
                    $"FAK-{number:D4}",
                    "Invoice",
                    NodeDecimal(row["netAmount"]) ?? 0m,
                    NodeText(row["date"]) ?? string.Empty,
                    remainder == 0m ? "Paid" : "Unpaid",
                    $"{BaseUrl}invoices/booked/{number}");
            })
            .ToArray();
    }

    public async Task<Stream?> GetDocumentStreamAsync(string tenantId, string documentId)
    {
        if (string.IsNullOrWhiteSpace(configuration["Integrations:Economic:AppSecretToken"])) return null;
        try
        {
            using var client = await CreateClientAsync(tenantId, CancellationToken.None);
            using var response = await client.GetAsync($"invoices/booked/{Uri.EscapeDataString(documentId)}/pdf");
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return new MemoryStream(bytes, writable: false);
        }
        catch
        {
            return null;
        }
    }

    [Obsolete("Use operational invoice draft synchronization instead of pushing raw hours.")]
    public Task<bool> SyncHoursAsync(string tenantId, object hoursData)
    {
        _ = tenantId;
        _ = hoursData;
        return Task.FromResult(false);
    }

    private async Task<HttpClient> CreateClientAsync(string tenantId, CancellationToken cancellationToken)
    {
        var grantToken = configuration[$"Integrations:Economic:Agreements:{tenantId}:GrantToken"];
        if (string.IsNullOrWhiteSpace(grantToken) && connectionStore is not null && Guid.TryParse(tenantId, out var organizationId))
            grantToken = await connectionStore.GetAgreementGrantTokenAsync(organizationId, cancellationToken);

        if (string.IsNullOrWhiteSpace(grantToken))
            throw new InvalidOperationException("e-conomic is not connected for this organization.");

        return CreateClientWithGrantToken(grantToken);
    }

    private HttpClient CreateClientWithGrantToken(string agreementGrantToken)
    {
        var appSecret = configuration["Integrations:Economic:AppSecretToken"];
        if (string.IsNullOrWhiteSpace(appSecret))
            throw new InvalidOperationException("e-conomic app secret is not configured.");

        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(BaseUrl);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-AppSecretToken", appSecret);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-AgreementGrantToken", agreementGrantToken);
        return client;
    }

    private async Task<JsonObject> CreateCustomerPayloadAsync(
        HttpClient client,
        ExternalAccountingCustomer customer,
        CancellationToken cancellationToken)
    {
        var group = await ResolveDefaultNumberAsync(
            client,
            "Integrations:Economic:Defaults:CustomerGroupNumber",
            "customer-groups?pagesize=100",
            "customerGroupNumber",
            row => !NodeBool(row["barred"]),
            cancellationToken);
        var paymentTerms = await ResolveDefaultNumberAsync(
            client,
            "Integrations:Economic:Defaults:PaymentTermsNumber",
            "payment-terms?pagesize=100",
            "paymentTermsNumber",
            _ => true,
            cancellationToken);
        var vatZone = await ResolveDefaultNumberAsync(
            client,
            "Integrations:Economic:Defaults:VatZoneNumber",
            "vat-zones?pagesize=100",
            "vatZoneNumber",
            row => string.Equals(NodeText(row["name"]), "Domestic", StringComparison.OrdinalIgnoreCase),
            cancellationToken);

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

    private async Task<int> ResolveDefaultNumberAsync(
        HttpClient client,
        string configurationKey,
        string endpoint,
        string numberProperty,
        Func<JsonObject, bool> preferred,
        CancellationToken cancellationToken)
    {
        if (int.TryParse(configuration[configurationKey], NumberStyles.Integer, CultureInfo.InvariantCulture, out var configured) && configured > 0)
            return configured;

        var rows = await GetCollectionAsync(client, endpoint, cancellationToken);
        var selected = rows.FirstOrDefault(preferred) ?? rows.FirstOrDefault();
        var number = selected is null ? null : NodeInt(selected[numberProperty]);
        return number is > 0
            ? number.Value
            : throw new InvalidOperationException($"e-conomic has no usable default for '{numberProperty}'. Configure '{configurationKey}' explicitly.");
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

    private static ExternalAccountingCustomer ToExternalCustomer(JsonObject row)
    {
        var number = NodeText(row["customerNumber"]) ?? string.Empty;
        return new ExternalAccountingCustomer(
            number,
            NodeText(row["name"]) ?? $"Kunde {number}",
            NodeText(row["address"]),
            NodeText(row["zip"]),
            NodeText(row["city"]),
            NodeText(row["country"]),
            NodeText(row["email"]),
            null,
            NodeText(row["telephoneAndFaxNumber"]) ?? NodeText(row["mobilePhone"]));
    }

    private static AccountingInvoiceState ToDraftState(JsonObject row, string reference)
    {
        var number = NodeInt(row["draftInvoiceNumber"]);
        return new AccountingInvoiceState(
            number,
            null,
            "Draft",
            reference,
            number is null ? null : $"{BaseUrl}invoices/drafts/{number}",
            NodeDecimal(row["netAmount"]) ?? CalculateNet(row),
            null,
            ParseDate(row["dueDate"]));
    }

    private static AccountingInvoiceState ToBookedState(JsonObject row, string reference)
    {
        var number = NodeInt(row["bookedInvoiceNumber"]);
        var remainder = NodeDecimal(row["remainder"]);
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
            NodeDecimal(row["netAmount"]) ?? CalculateNet(row),
            remainder,
            dueDate);
    }

    private static decimal CalculateNet(JsonObject row)
    {
        if (row["lines"] is not JsonArray lines) return 0m;
        return lines.OfType<JsonObject>().Sum(line =>
            (NodeDecimal(line["quantity"]) ?? 0m) *
            (NodeDecimal(line["unitNetPrice"]) ?? 0m) *
            (1m - ((NodeDecimal(line["discountPercentage"]) ?? 0m) / 100m)));
    }

    private static async Task<IReadOnlyList<JsonObject>> GetCollectionAsync(
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
            next = NodeText(root["pagination"]?["nextPage"]);
        }

        return result;
    }

    private static async Task<JsonObject> ParseObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("e-conomic returned an unexpected JSON payload.");
    }

    private string? ProductNumberOrNull(string kind)
    {
        var suffix = kind switch
        {
            "hours" => "Hours",
            "material" => "Material",
            "outlay" => "Outlay",
            _ => throw new InvalidOperationException($"Unsupported invoice line kind '{kind}'.")
        };
        return configuration[$"Integrations:Economic:Products:{suffix}"];
    }

    private static string? NodeText(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text;
            if (value.TryGetValue<int>(out var number)) return number.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<long>(out var longNumber)) return longNumber.ToString(CultureInfo.InvariantCulture);
        }
        return node.ToJsonString().Trim('"');
    }

    private static int? NodeInt(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<int>(out var number)) return number;
        return int.TryParse(NodeText(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) ? number : null;
    }

    private static decimal? NodeDecimal(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<decimal>(out var number)) return number;
        return decimal.TryParse(NodeText(node), NumberStyles.Number, CultureInfo.InvariantCulture, out number) ? number : null;
    }

    private static bool NodeBool(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    private static DateOnly? ParseDate(JsonNode? node) =>
        DateOnly.TryParse(NodeText(node), CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null;
}
