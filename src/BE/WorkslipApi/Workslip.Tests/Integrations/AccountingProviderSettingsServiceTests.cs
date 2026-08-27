using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Integrations;
using Workslip.Application.Organizations;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Integrations;

public sealed class AccountingProviderSettingsServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsSelectedProviderAndHidesDevelopmentMock()
    {
        var organizationId = Guid.NewGuid();
        var repository = new FakeOrganizationRepository(CreateOrganization(organizationId));
        var store = new FakeConfigurationStore("economics");
        var service = CreateService(
            organizationId,
            repository,
            store,
            new FakeAccountingProvider("mock", "Mock"),
            new FakeAccountingProvider("economics", "e-conomic"));

        var result = await service.GetAsync(CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal("economics", result.Value.ProviderId);
        var provider = Assert.Single(result.Value.Providers);
        Assert.Equal("economics", provider.Id);
        Assert.Equal("e-conomic", provider.DisplayName);
    }

    [Fact]
    public async Task UpdateAsync_WhenProviderIsUnsupported_ReturnsInvalidWithoutPersisting()
    {
        var organizationId = Guid.NewGuid();
        var repository = new FakeOrganizationRepository(CreateOrganization(organizationId));
        var store = new FakeConfigurationStore();
        var service = CreateService(
            organizationId,
            repository,
            store,
            new FakeAccountingProvider("economics", "e-conomic"));

        var result = await service.UpdateAsync(
            new UpdateAccountingProviderRequest("unknown-provider"),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(0, store.SetCalls);
    }

    [Fact]
    public async Task UpdateAsync_PersistsCanonicalProviderIdForCurrentOrganization()
    {
        var organizationId = Guid.NewGuid();
        var repository = new FakeOrganizationRepository(CreateOrganization(organizationId));
        var store = new FakeConfigurationStore();
        var service = CreateService(
            organizationId,
            repository,
            store,
            new FakeAccountingProvider("economics", "e-conomic"));

        var result = await service.UpdateAsync(
            new UpdateAccountingProviderRequest(" ECONOMICS "),
            CancellationToken.None);

        Assert.Equal(ResultStatus.NoContent, result.Status);
        Assert.Equal(1, store.SetCalls);
        Assert.Equal(organizationId, store.OrganizationId);
        Assert.Equal("economics", store.ProviderId);
    }

    private static AccountingProviderSettingsService CreateService(
        Guid organizationId,
        IOrganizationRepository repository,
        IAccountingProviderConfigurationStore store,
        params IAccountingProvider[] providers) =>
        new(
            repository,
            store,
            providers,
            new FakeCurrentUserContext(organizationId));

    private static OrganizationRow CreateOrganization(Guid id) =>
        new()
        {
            Id = id,
            Name = "Test organization",
            Cvr = "12345678",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private sealed class FakeCurrentUserContext(Guid organizationId) : ICurrentUserContext
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? OrganizationId => organizationId;
        public string? Role => "Admin";
    }

    private sealed class FakeOrganizationRepository(OrganizationRow organization) : IOrganizationRepository
    {
        public Task<bool> CvrExistsAsync(string normalizedCvr, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<OrganizationOnboardingResponse?> CreateAsync(
            CreateOrganizationRequest request,
            string normalizedCvr,
            CancellationToken cancellationToken) =>
            Task.FromResult<OrganizationOnboardingResponse?>(null);

        public Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<CurrentUserResponse?>(null);

        public Task<OrganizationRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<OrganizationRow?>(id == organization.Id ? organization : null);
    }

    private sealed class FakeConfigurationStore(string? providerId = null) : IAccountingProviderConfigurationStore
    {
        public int SetCalls { get; private set; }
        public Guid? OrganizationId { get; private set; }
        public string? ProviderId { get; private set; } = providerId;

        public Task<string?> GetProviderAsync(
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ProviderId);

        public Task<bool> SetProviderAsync(
            Guid organizationId,
            string? nextProviderId,
            CancellationToken cancellationToken)
        {
            SetCalls++;
            OrganizationId = organizationId;
            ProviderId = nextProviderId;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeAccountingProvider(string providerId, string displayName) : IAccountingProvider
    {
        public string ProviderId => providerId;
        public string DisplayName => displayName;

        public Task<bool> TestConnectionAsync(string tenantId) => Task.FromResult(true);

        public Task<IEnumerable<AccountingDocument>> GetDocumentsForUserAsync(
            string tenantId,
            string userId,
            string startDate,
            string endDate) =>
            Task.FromResult<IEnumerable<AccountingDocument>>(Array.Empty<AccountingDocument>());

        public Task<Stream> GetDocumentStreamAsync(string tenantId, string documentId) =>
            Task.FromResult<Stream>(Stream.Null);

        public Task<bool> SyncHoursAsync(string tenantId, object hoursData) => Task.FromResult(true);
    }
}
