using Workslip.Application.Auth;
using Workslip.Application.Integrations;
using Xunit;

namespace Workslip.Tests.Integrations;

public sealed class AccountingOperationsServiceTests
{
    private static readonly Guid OrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid JobId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CustomerId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Existing_invoice_link_is_idempotent_and_does_not_create_again()
    {
        var existing = new JobAccountingInvoiceResponse(
            JobId, "economics", 42, null, "Draft", $"WS-{JobId:N}", "https://example.invalid/draft/42", 1200m, DateTimeOffset.UtcNow);
        var repository = new FakeRepository { ExistingInvoice = existing };
        var provider = new FakeProvider();
        var service = CreateService(repository, provider);

        var result = await service.CreateDraftInvoiceAsync(JobId, CancellationToken.None);

        Assert.Same(existing, result);
        Assert.Equal(0, provider.CreateDraftCalls);
        Assert.Equal(0, repository.InvoiceSourceCalls);
    }

    [Fact]
    public async Task Non_approved_job_is_rejected_before_external_write()
    {
        var repository = new FakeRepository
        {
            InvoiceSource = Source(status: "InReview", missingRateHours: 0m)
        };
        var provider = new FakeProvider();
        var service = CreateService(repository, provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateDraftInvoiceAsync(JobId, CancellationToken.None));

        Assert.Contains("approved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.CreateDraftCalls);
        Assert.Equal(0, provider.UpsertCustomerCalls);
    }

    [Fact]
    public async Task Approved_job_with_unpriced_hours_is_rejected_before_external_write()
    {
        var repository = new FakeRepository
        {
            InvoiceSource = Source(status: "Approved", missingRateHours: 1.5m)
        };
        var provider = new FakeProvider();
        var service = CreateService(repository, provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateDraftInvoiceAsync(JobId, CancellationToken.None));

        Assert.Contains("missing a billable rate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, provider.CreateDraftCalls);
        Assert.Equal(0, provider.UpsertCustomerCalls);
    }

    [Fact]
    public async Task Approved_priced_job_creates_one_draft_and_persists_link()
    {
        var repository = new FakeRepository
        {
            InvoiceSource = Source(status: "Approved", missingRateHours: 0m),
            CustomerLink = new AccountingCustomerLink("1001", DateTimeOffset.UtcNow)
        };
        var provider = new FakeProvider();
        var service = CreateService(repository, provider);

        var result = await service.CreateDraftInvoiceAsync(JobId, CancellationToken.None);

        Assert.Equal("Draft", result.Status);
        Assert.Equal(1, provider.CreateDraftCalls);
        Assert.Equal(1, repository.UpsertInvoiceCalls);
        Assert.NotNull(provider.LastDraftRequest);
        Assert.Equal($"WS-{JobId:N}", provider.LastDraftRequest!.ExternalReference);
        Assert.Equal("1001", provider.LastDraftRequest.ExternalCustomerNumber);
        Assert.Equal(900m, provider.LastDraftRequest.Lines.Single().UnitNetPrice);
    }

    private static AccountingOperationsService CreateService(FakeRepository repository, FakeProvider provider) =>
        new(
            new FakeEngine(provider),
            repository,
            new FakeCurrentUser { OrganizationId = OrganizationId, UserId = Guid.NewGuid(), Role = "Admin" });

    private static AccountingInvoiceSource Source(string status, decimal missingRateHours) =>
        new(
            JobId,
            status,
            "1042",
            CustomerId,
            new AccountingLocalCustomer(
                CustomerId,
                "1001",
                "Testkunde",
                "Testvej 1",
                "7400",
                "Herning",
                "Danmark",
                "kunde@example.invalid",
                "Kunde",
                "12345678"),
            [new AccountingDraftInvoiceLine("hours", "Timer – Montør", 2m, 900m)],
            missingRateHours);

    private sealed class FakeCurrentUser : ICurrentUserContext
    {
        public Guid? UserId { get; init; }
        public Guid? OrganizationId { get; init; }
        public string? Role { get; init; }
    }

    private sealed class FakeEngine(FakeProvider provider) : IIntegrationEngine
    {
        public Task<IAccountingProvider> GetAccountingProviderAsync(string tenantId) => Task.FromResult<IAccountingProvider>(provider);
        public Task<IAccountingOperationsProvider> GetAccountingOperationsProviderAsync(string tenantId) => Task.FromResult<IAccountingOperationsProvider>(provider);
        public IEnumerable<IIntegrationProvider> GetAvailableProviders() => [provider];
    }

    private sealed class FakeProvider : IAccountingOperationsProvider
    {
        public string ProviderId => "economics";
        public string DisplayName => "e-conomic";
        public int CreateDraftCalls { get; private set; }
        public int UpsertCustomerCalls { get; private set; }
        public AccountingDraftInvoiceRequest? LastDraftRequest { get; private set; }

        public bool IsConfigured(string tenantId) => true;
        public Task<bool> TestConnectionAsync(string tenantId) => Task.FromResult(true);
        public Task<IReadOnlyList<ExternalAccountingCustomer>> GetCustomersAsync(string tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExternalAccountingCustomer>>([]);

        public Task<ExternalAccountingCustomer> UpsertCustomerAsync(string tenantId, ExternalAccountingCustomer customer, CancellationToken cancellationToken)
        {
            UpsertCustomerCalls++;
            return Task.FromResult(customer with { ExternalCustomerNumber = string.IsNullOrWhiteSpace(customer.ExternalCustomerNumber) ? "1001" : customer.ExternalCustomerNumber });
        }

        public Task<AccountingInvoiceState> CreateDraftInvoiceAsync(string tenantId, AccountingDraftInvoiceRequest request, CancellationToken cancellationToken)
        {
            CreateDraftCalls++;
            LastDraftRequest = request;
            return Task.FromResult(new AccountingInvoiceState(
                42,
                null,
                "Draft",
                request.ExternalReference,
                "https://example.invalid/draft/42",
                request.Lines.Sum(line => line.Quantity * line.UnitNetPrice),
                null,
                request.Date.AddDays(14)));
        }

        public Task<AccountingInvoiceState?> FindInvoiceByReferenceAsync(string tenantId, string externalReference, CancellationToken cancellationToken) =>
            Task.FromResult<AccountingInvoiceState?>(null);

        public Task<IEnumerable<AccountingDocument>> GetDocumentsForUserAsync(string tenantId, string userId, string startDate, string endDate) =>
            Task.FromResult<IEnumerable<AccountingDocument>>([]);

        public Task<Stream?> GetDocumentStreamAsync(string tenantId, string documentId) => Task.FromResult<Stream?>(null);
        public Task<bool> SyncHoursAsync(string tenantId, object hoursData) => Task.FromResult(false);
    }

    private sealed class FakeRepository : IAccountingSyncRepository
    {
        public JobAccountingInvoiceResponse? ExistingInvoice { get; init; }
        public AccountingInvoiceSource? InvoiceSource { get; init; }
        public AccountingCustomerLink? CustomerLink { get; init; }
        public int InvoiceSourceCalls { get; private set; }
        public int UpsertInvoiceCalls { get; private set; }
        private JobAccountingInvoiceResponse? _persisted;

        public Task<JobAccountingInvoiceResponse?> GetJobInvoiceLinkAsync(Guid organizationId, Guid jobId, string providerId, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingInvoice ?? _persisted);

        public Task<AccountingInvoiceSource?> GetInvoiceSourceAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
        {
            InvoiceSourceCalls++;
            return Task.FromResult(InvoiceSource);
        }

        public Task<AccountingCustomerLink?> GetCustomerLinkAsync(Guid organizationId, Guid customerId, string providerId, CancellationToken cancellationToken) =>
            Task.FromResult(CustomerLink);

        public Task UpsertJobInvoiceLinkAsync(Guid organizationId, Guid jobId, string providerId, AccountingInvoiceState state, CancellationToken cancellationToken)
        {
            UpsertInvoiceCalls++;
            _persisted = new JobAccountingInvoiceResponse(
                jobId,
                providerId,
                state.DraftInvoiceNumber,
                state.BookedInvoiceNumber,
                state.Status,
                state.ExternalReference,
                state.ExternalUrl,
                state.NetAmount,
                DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AccountingLocalCustomer>> ListCustomersAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingLocalCustomer?> GetCustomerAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> UpsertLocalCustomerAsync(Guid organizationId, ExternalAccountingCustomer customer, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertCustomerLinkAsync(Guid organizationId, Guid customerId, string providerId, string externalCustomerNumber, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<JobBillableItemResponse>> ListBillableItemsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobBillableItemResponse> UpsertBillableItemAsync(Guid organizationId, Guid jobId, Guid? itemId, UpsertJobBillableItemRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteBillableItemAsync(Guid organizationId, Guid jobId, Guid itemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task LinkDocumentAsync(Guid organizationId, Guid jobId, string providerId, LinkAccountingDocumentRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobAccountingDocumentResponse>> ListLinkedDocumentsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
