using Workslip.Application.Auth;

namespace Workslip.Application.Integrations;

public sealed record ExternalAccountingCustomer(
    string ExternalCustomerNumber,
    string Name,
    string? Address,
    string? ZipCode,
    string? City,
    string? Country,
    string? Email,
    string? ContactPerson,
    string? Phone);

public sealed record AccountingLocalCustomer(
    Guid Id,
    string? CustomerNumber,
    string Name,
    string? Address,
    string? ZipCode,
    string? City,
    string? Country,
    string? Email,
    string? ContactPerson,
    string? Phone);

public sealed record AccountingDraftInvoiceLine(
    string Kind,
    string Description,
    decimal Quantity,
    decimal UnitNetPrice);

public sealed record AccountingDraftInvoiceRequest(
    Guid JobId,
    string ExternalCustomerNumber,
    string Currency,
    DateOnly Date,
    string ExternalReference,
    string Heading,
    IReadOnlyList<AccountingDraftInvoiceLine> Lines);

public sealed record AccountingInvoiceState(
    int? DraftInvoiceNumber,
    int? BookedInvoiceNumber,
    string Status,
    string ExternalReference,
    string? ExternalUrl,
    decimal NetAmount,
    decimal? Remainder,
    DateOnly? DueDate);

public sealed record AccountingConnectionStatusResponse(
    string ProviderId,
    string ProviderDisplayName,
    bool Configured,
    bool Connected);

public sealed record AccountingCustomerSyncResponse(
    int Pulled,
    int Pushed,
    int Linked,
    int TotalLocal,
    int TotalExternal);

public sealed record JobAccountingInvoiceResponse(
    Guid JobId,
    string ProviderId,
    int? DraftInvoiceNumber,
    int? BookedInvoiceNumber,
    string Status,
    string ExternalReference,
    string? ExternalUrl,
    decimal NetAmount,
    DateTimeOffset LastSyncedAt);

public sealed record JobBillableItemResponse(
    Guid Id,
    Guid JobId,
    string Kind,
    string Description,
    decimal Quantity,
    decimal UnitNetPrice,
    decimal LineNetAmount,
    string Source);

public sealed record UpsertJobBillableItemRequest(
    string Kind,
    string Description,
    decimal Quantity,
    decimal UnitNetPrice,
    string? Source = null);

public sealed record LinkAccountingDocumentRequest(
    string ExternalDocumentId,
    string DocumentNumber,
    string DocumentType,
    decimal Amount,
    DateOnly DocumentDate,
    string Status,
    string? ExternalUrl);

public sealed record JobAccountingDocumentResponse(
    Guid JobId,
    string ProviderId,
    string ExternalDocumentId,
    string DocumentNumber,
    string DocumentType,
    decimal Amount,
    DateOnly DocumentDate,
    string Status,
    string? ExternalUrl,
    DateTimeOffset LinkedAt);

public sealed record AccountingInvoiceSource(
    Guid JobId,
    string Status,
    string? ReportNumber,
    Guid CustomerId,
    AccountingLocalCustomer Customer,
    IReadOnlyList<AccountingDraftInvoiceLine> Lines,
    decimal MissingRateHours);

public sealed record AccountingCustomerLink(string ExternalCustomerNumber, DateTimeOffset LastSyncedAt);

public interface IAccountingOperationsProvider : IAccountingProvider
{
    bool IsConfigured(string tenantId);
    Task<IReadOnlyList<ExternalAccountingCustomer>> GetCustomersAsync(string tenantId, CancellationToken cancellationToken);
    Task<ExternalAccountingCustomer> UpsertCustomerAsync(string tenantId, ExternalAccountingCustomer customer, CancellationToken cancellationToken);
    Task<AccountingInvoiceState> CreateDraftInvoiceAsync(string tenantId, AccountingDraftInvoiceRequest request, CancellationToken cancellationToken);
    Task<AccountingInvoiceState?> FindInvoiceByReferenceAsync(string tenantId, string externalReference, CancellationToken cancellationToken);
}

public interface IAccountingSyncRepository
{
    Task<IReadOnlyList<AccountingLocalCustomer>> ListCustomersAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<AccountingLocalCustomer?> GetCustomerAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken);
    Task<Guid> UpsertLocalCustomerAsync(Guid organizationId, ExternalAccountingCustomer customer, CancellationToken cancellationToken);
    Task<AccountingCustomerLink?> GetCustomerLinkAsync(Guid organizationId, Guid customerId, string providerId, CancellationToken cancellationToken);
    Task UpsertCustomerLinkAsync(Guid organizationId, Guid customerId, string providerId, string externalCustomerNumber, CancellationToken cancellationToken);
    Task<AccountingInvoiceSource?> GetInvoiceSourceAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);
    Task<JobAccountingInvoiceResponse?> GetJobInvoiceLinkAsync(Guid organizationId, Guid jobId, string providerId, CancellationToken cancellationToken);
    Task UpsertJobInvoiceLinkAsync(Guid organizationId, Guid jobId, string providerId, AccountingInvoiceState state, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobBillableItemResponse>> ListBillableItemsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);
    Task<JobBillableItemResponse> UpsertBillableItemAsync(Guid organizationId, Guid jobId, Guid? itemId, UpsertJobBillableItemRequest request, CancellationToken cancellationToken);
    Task DeleteBillableItemAsync(Guid organizationId, Guid jobId, Guid itemId, CancellationToken cancellationToken);
    Task LinkDocumentAsync(Guid organizationId, Guid jobId, string providerId, LinkAccountingDocumentRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobAccountingDocumentResponse>> ListLinkedDocumentsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);
}

public interface IAccountingOperationsService
{
    Task<AccountingConnectionStatusResponse> GetStatusAsync(CancellationToken cancellationToken);
    Task<AccountingCustomerSyncResponse> SyncCustomersAsync(CancellationToken cancellationToken);
    Task<JobAccountingInvoiceResponse> CreateDraftInvoiceAsync(Guid jobId, CancellationToken cancellationToken);
    Task<JobAccountingInvoiceResponse?> RefreshInvoiceAsync(Guid jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobBillableItemResponse>> ListBillableItemsAsync(Guid jobId, CancellationToken cancellationToken);
    Task<JobBillableItemResponse> UpsertBillableItemAsync(Guid jobId, Guid? itemId, UpsertJobBillableItemRequest request, CancellationToken cancellationToken);
    Task DeleteBillableItemAsync(Guid jobId, Guid itemId, CancellationToken cancellationToken);
    Task LinkDocumentAsync(Guid jobId, LinkAccountingDocumentRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobAccountingDocumentResponse>> ListLinkedDocumentsAsync(Guid jobId, CancellationToken cancellationToken);
}

public sealed class AccountingOperationsService(
    IIntegrationEngine integrationEngine,
    IAccountingSyncRepository repository,
    ICurrentUserContext currentUser) : IAccountingOperationsService
{
    public async Task<AccountingConnectionStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var (organizationId, tenantId, provider) = await ResolveAsync();
        _ = organizationId;
        var configured = provider.IsConfigured(tenantId);
        var connected = configured && await provider.TestConnectionAsync(tenantId);
        return new AccountingConnectionStatusResponse(provider.ProviderId, provider.DisplayName, configured, connected);
    }

    public async Task<AccountingCustomerSyncResponse> SyncCustomersAsync(CancellationToken cancellationToken)
    {
        var (organizationId, tenantId, provider) = await ResolveAsync();
        EnsureConfigured(provider, tenantId);

        var external = await provider.GetCustomersAsync(tenantId, cancellationToken);
        var pulled = 0;
        var linked = 0;

        foreach (var remote in external)
        {
            var localId = await repository.UpsertLocalCustomerAsync(organizationId, remote, cancellationToken);
            await repository.UpsertCustomerLinkAsync(organizationId, localId, provider.ProviderId, remote.ExternalCustomerNumber, cancellationToken);
            pulled++;
            linked++;
        }

        var local = await repository.ListCustomersAsync(organizationId, cancellationToken);
        var externalByNumber = external.ToDictionary(x => x.ExternalCustomerNumber, StringComparer.OrdinalIgnoreCase);
        var pushed = 0;

        foreach (var customer in local)
        {
            var link = await repository.GetCustomerLinkAsync(organizationId, customer.Id, provider.ProviderId, cancellationToken);
            ExternalAccountingCustomer desired;

            if (link is null && customer.CustomerNumber is not null && externalByNumber.TryGetValue(customer.CustomerNumber, out var matched))
            {
                await repository.UpsertCustomerLinkAsync(organizationId, customer.Id, provider.ProviderId, matched.ExternalCustomerNumber, cancellationToken);
                link = new AccountingCustomerLink(matched.ExternalCustomerNumber, DateTimeOffset.UtcNow);
                linked++;
            }

            desired = new ExternalAccountingCustomer(
                link?.ExternalCustomerNumber ?? string.Empty,
                customer.Name,
                customer.Address,
                customer.ZipCode,
                customer.City,
                customer.Country,
                customer.Email,
                customer.ContactPerson,
                customer.Phone);

            var synced = await provider.UpsertCustomerAsync(tenantId, desired, cancellationToken);
            await repository.UpsertCustomerLinkAsync(organizationId, customer.Id, provider.ProviderId, synced.ExternalCustomerNumber, cancellationToken);
            pushed++;
            if (link is null) linked++;
        }

        return new AccountingCustomerSyncResponse(pulled, pushed, linked, local.Count, external.Count);
    }

    public async Task<JobAccountingInvoiceResponse> CreateDraftInvoiceAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var (organizationId, tenantId, provider) = await ResolveAsync();
        EnsureConfigured(provider, tenantId);

        var existing = await repository.GetJobInvoiceLinkAsync(organizationId, jobId, provider.ProviderId, cancellationToken);
        if (existing is not null)
            return existing;

        var source = await repository.GetInvoiceSourceAsync(organizationId, jobId, cancellationToken)
            ?? throw new KeyNotFoundException("Job not found in the current organization.");

        if (!string.Equals(source.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only approved jobs can create an accounting invoice draft.");
        if (source.MissingRateHours > 0)
            throw new InvalidOperationException($"{source.MissingRateHours:0.###} registered hours are missing a billable rate.");
        if (source.Lines.Count == 0)
            throw new InvalidOperationException("The job has no billable hours, materials or outlays.");

        var customerLink = await repository.GetCustomerLinkAsync(organizationId, source.CustomerId, provider.ProviderId, cancellationToken);
        if (customerLink is null)
        {
            var synced = await provider.UpsertCustomerAsync(
                tenantId,
                new ExternalAccountingCustomer(
                    string.Empty,
                    source.Customer.Name,
                    source.Customer.Address,
                    source.Customer.ZipCode,
                    source.Customer.City,
                    source.Customer.Country,
                    source.Customer.Email,
                    source.Customer.ContactPerson,
                    source.Customer.Phone),
                cancellationToken);
            await repository.UpsertCustomerLinkAsync(organizationId, source.CustomerId, provider.ProviderId, synced.ExternalCustomerNumber, cancellationToken);
            customerLink = new AccountingCustomerLink(synced.ExternalCustomerNumber, DateTimeOffset.UtcNow);
        }

        var reference = $"WS-{jobId:N}";
        var existingExternal = await provider.FindInvoiceByReferenceAsync(tenantId, reference, cancellationToken);
        var state = existingExternal ?? await provider.CreateDraftInvoiceAsync(
            tenantId,
            new AccountingDraftInvoiceRequest(
                jobId,
                customerLink.ExternalCustomerNumber,
                "DKK",
                DateOnly.FromDateTime(DateTime.UtcNow),
                reference,
                source.ReportNumber is null ? "Workslip sag" : $"Workslip SAG-{source.ReportNumber}",
                source.Lines),
            cancellationToken);

        await repository.UpsertJobInvoiceLinkAsync(organizationId, jobId, provider.ProviderId, state, cancellationToken);
        return await repository.GetJobInvoiceLinkAsync(organizationId, jobId, provider.ProviderId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice link could not be persisted.");
    }

    public async Task<JobAccountingInvoiceResponse?> RefreshInvoiceAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var (organizationId, tenantId, provider) = await ResolveAsync();
        EnsureConfigured(provider, tenantId);
        var link = await repository.GetJobInvoiceLinkAsync(organizationId, jobId, provider.ProviderId, cancellationToken);
        if (link is null) return null;

        var state = await provider.FindInvoiceByReferenceAsync(tenantId, link.ExternalReference, cancellationToken);
        if (state is null) return link;
        await repository.UpsertJobInvoiceLinkAsync(organizationId, jobId, provider.ProviderId, state, cancellationToken);
        return await repository.GetJobInvoiceLinkAsync(organizationId, jobId, provider.ProviderId, cancellationToken);
    }

    public async Task<IReadOnlyList<JobBillableItemResponse>> ListBillableItemsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganization();
        return await repository.ListBillableItemsAsync(organizationId, jobId, cancellationToken);
    }

    public async Task<JobBillableItemResponse> UpsertBillableItemAsync(Guid jobId, Guid? itemId, UpsertJobBillableItemRequest request, CancellationToken cancellationToken)
    {
        if (request.Kind is not ("material" or "outlay"))
            throw new ArgumentException("Kind must be 'material' or 'outlay'.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ArgumentException("Description is required.", nameof(request));
        if (request.Quantity <= 0 || request.UnitNetPrice < 0)
            throw new ArgumentException("Quantity must be positive and unit price non-negative.", nameof(request));
        return await repository.UpsertBillableItemAsync(RequireOrganization(), jobId, itemId, request, cancellationToken);
    }

    public Task DeleteBillableItemAsync(Guid jobId, Guid itemId, CancellationToken cancellationToken) =>
        repository.DeleteBillableItemAsync(RequireOrganization(), jobId, itemId, cancellationToken);

    public async Task LinkDocumentAsync(Guid jobId, LinkAccountingDocumentRequest request, CancellationToken cancellationToken)
    {
        var (_, _, provider) = await ResolveAsync();
        await repository.LinkDocumentAsync(RequireOrganization(), jobId, provider.ProviderId, request, cancellationToken);
    }

    public Task<IReadOnlyList<JobAccountingDocumentResponse>> ListLinkedDocumentsAsync(Guid jobId, CancellationToken cancellationToken) =>
        repository.ListLinkedDocumentsAsync(RequireOrganization(), jobId, cancellationToken);

    private async Task<(Guid OrganizationId, string TenantId, IAccountingOperationsProvider Provider)> ResolveAsync()
    {
        var organizationId = RequireOrganization();
        var tenantId = organizationId.ToString();
        var provider = await integrationEngine.GetAccountingOperationsProviderAsync(tenantId);
        return (organizationId, tenantId, provider);
    }

    private Guid RequireOrganization() =>
        currentUser.OrganizationId ?? throw new UnauthorizedAccessException("Missing organization context.");

    private static void EnsureConfigured(IAccountingOperationsProvider provider, string tenantId)
    {
        if (!provider.IsConfigured(tenantId))
            throw new InvalidOperationException($"Accounting provider '{provider.DisplayName}' is not configured for this organization.");
    }
}
