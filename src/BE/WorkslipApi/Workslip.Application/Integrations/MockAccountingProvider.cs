using System.Text;

namespace Workslip.Application.Integrations;

public sealed class MockAccountingProvider : IAccountingOperationsProvider
{
    public string ProviderId => "mock";
    public string DisplayName => "Mock Accounting (Dev)";

    public bool IsConfigured(string tenantId) => true;
    public Task<bool> TestConnectionAsync(string tenantId) => Task.FromResult(true);

    public Task<IReadOnlyList<ExternalAccountingCustomer>> GetCustomersAsync(string tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExternalAccountingCustomer>>(Array.Empty<ExternalAccountingCustomer>());

    public Task<ExternalAccountingCustomer> UpsertCustomerAsync(
        string tenantId,
        ExternalAccountingCustomer customer,
        CancellationToken cancellationToken)
    {
        var number = string.IsNullOrWhiteSpace(customer.ExternalCustomerNumber)
            ? Math.Abs(HashCode.Combine(tenantId, customer.Name, customer.Email)).ToString()
            : customer.ExternalCustomerNumber;
        return Task.FromResult(customer with { ExternalCustomerNumber = number });
    }

    public Task<AccountingInvoiceState> CreateDraftInvoiceAsync(
        string tenantId,
        AccountingDraftInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var draftNumber = Math.Abs(HashCode.Combine(tenantId, request.JobId)) % 900000 + 100000;
        var amount = request.Lines.Sum(line => line.Quantity * line.UnitNetPrice);
        return Task.FromResult(new AccountingInvoiceState(
            draftNumber,
            null,
            "Draft",
            request.ExternalReference,
            $"https://economics.mock/invoices/drafts/{draftNumber}",
            amount,
            null,
            request.Date.AddDays(14)));
    }

    public Task<AccountingInvoiceState?> FindInvoiceByReferenceAsync(
        string tenantId,
        string externalReference,
        CancellationToken cancellationToken) => Task.FromResult<AccountingInvoiceState?>(null);

    public Task<IEnumerable<AccountingDocument>> GetDocumentsForUserAsync(
        string tenantId,
        string userId,
        string startDate,
        string endDate)
    {
        var random = new Random(HashCode.Combine(tenantId, userId));
        var docs = Enumerable.Range(0, random.Next(0, 4))
            .Select(index => new AccountingDocument(
                $"doc-{index}",
                $"BIL-{random.Next(1000, 9999)}",
                "Receipt",
                random.Next(100, 5000),
                $"{startDate[..Math.Min(7, startDate.Length)]}-{random.Next(1, 28):D2}",
                "Pending",
                $"https://economics.mock/doc/{index}"))
            .ToArray();
        return Task.FromResult<IEnumerable<AccountingDocument>>(docs);
    }

    public Task<Stream?> GetDocumentStreamAsync(string tenantId, string documentId)
    {
        Stream stream = new MemoryStream(Encoding.UTF8.GetBytes($"Mock accounting document {documentId}"), writable: false);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> SyncHoursAsync(string tenantId, object hoursData) => Task.FromResult(true);
}
