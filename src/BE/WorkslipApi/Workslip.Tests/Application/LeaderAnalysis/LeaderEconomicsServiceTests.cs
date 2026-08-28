using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workslip.Application.Auth;
using Workslip.Application.Integrations;
using Workslip.Application.LeaderAnalysis;
using Xunit;

namespace Workslip.Tests.Application.LeaderAnalysis;

public sealed class LeaderEconomicsServiceTests
{
    [Fact]
    public async Task GetSummaryAsync_CalculatesTotalsAndAveragesCorrectly()
    {
        // Arrange — 3 bilag: 2 fakturaer, 1 kvittering, beløb 100, 200, 300
        var docs = new[]
        {
            new AccountingDocument("1", "FAK-0001", "Invoice", 100m, "2022-06-01", "Paid", "https://example.com/1"),
            new AccountingDocument("2", "FAK-0002", "Invoice", 200m, "2022-06-02", "Unpaid", "https://example.com/2"),
            new AccountingDocument("3", "BIL-0001", "Receipt", 300m, "2022-06-03", "Pending", "https://example.com/3"),
        };
        var provider = new StubAccountingProvider(docs);
        var service = CreateService(provider);

        // Act
        var summary = await service.GetSummaryAsync(null, null, CancellationToken.None);

        // Assert — tal skal passe perfekt
        Assert.Equal("mock", summary.ProviderId);
        Assert.Equal(3, summary.DocumentCount);
        Assert.Equal(2, summary.InvoiceCount);
        Assert.Equal(1, summary.ReceiptCount);
        Assert.Equal(600m, summary.TotalAmount);
        Assert.Equal(200m, summary.AverageAmount); // 600/3
        Assert.Equal(3, summary.RecentDocuments.Count);
        // RecentDocuments sorteret desc på Date
        Assert.Equal("3", summary.RecentDocuments[0].DocumentId); // 2022-06-03
        Assert.Equal("2", summary.RecentDocuments[1].DocumentId);
        Assert.Equal("1", summary.RecentDocuments[2].DocumentId);
    }

    [Fact]
    public async Task GetSummaryAsync_HandlesEmptyCorrectly()
    {
        var provider = new StubAccountingProvider([]);
        var service = CreateService(provider);
        var summary = await service.GetSummaryAsync(null, null, CancellationToken.None);
        Assert.Equal(0, summary.DocumentCount);
        Assert.Equal(0, summary.TotalAmount);
        Assert.Equal(0, summary.AverageAmount);
        Assert.Empty(summary.RecentDocuments);
    }

    [Fact]
    public async Task GetSummaryAsync_TakesFiveMostRecent()
    {
        var docs = Enumerable.Range(1, 10).Select(i =>
            new AccountingDocument(i.ToString(), $"FAK-{i:D4}", "Invoice", i * 10m, $"2022-06-{i:D2}", "Paid", $"https://example.com/{i}")).ToArray();
        var provider = new StubAccountingProvider(docs);
        var service = CreateService(provider);
        var summary = await service.GetSummaryAsync(null, null, CancellationToken.None);
        Assert.Equal(10, summary.DocumentCount);
        Assert.Equal(5, summary.RecentDocuments.Count);
        Assert.Equal("10", summary.RecentDocuments[0].DocumentId); // nyeste
        Assert.Equal("6", summary.RecentDocuments[4].DocumentId);
    }

    [Fact]
    public async Task GetSummaryAsync_SumsWithDecimalPrecision()
    {
        // Test at decimal-præcision bevares (ingen float-fejl)
        var docs = new[]
        {
            new AccountingDocument("1", "FAK-1", "Invoice", 0.10m, "2022-06-01", "Paid", ""),
            new AccountingDocument("2", "FAK-2", "Invoice", 0.20m, "2022-06-02", "Paid", ""),
            new AccountingDocument("3", "FAK-3", "Invoice", 0.30m, "2022-06-03", "Paid", ""),
        };
        var provider = new StubAccountingProvider(docs);
        var service = CreateService(provider);
        var summary = await service.GetSummaryAsync(null, null, CancellationToken.None);
        Assert.Equal(0.60m, summary.TotalAmount);
        Assert.Equal(0.20m, summary.AverageAmount);
    }

    [Fact]
    public async Task GetDocumentsAsync_ReturnsAllDocumentsInDateDescOrder()
    {
        var docs = new[]
        {
            new AccountingDocument("1", "FAK-1", "Invoice", 100m, "2022-06-01", "Paid", ""),
            new AccountingDocument("2", "FAK-2", "Invoice", 200m, "2022-06-03", "Paid", ""),
            new AccountingDocument("3", "FAK-3", "Invoice", 300m, "2022-06-02", "Paid", ""),
        };
        var provider = new StubAccountingProvider(docs);
        var service = CreateService(provider);
        var result = await service.GetDocumentsAsync(null, null, CancellationToken.None);
        Assert.Equal(3, result.Documents.Count);
        Assert.Equal("2", result.Documents[0].DocumentId); // 2022-06-03
        Assert.Equal("3", result.Documents[1].DocumentId); // 2022-06-02
        Assert.Equal("1", result.Documents[2].DocumentId); // 2022-06-01
    }

    private static LeaderEconomicsService CreateService(IAccountingProvider provider)
    {
        var orgId = Guid.Parse("f5029edf-f2ad-3c47-1457-fd6bb75b3c01");
        var currentUser = new StubCurrentUserContext(orgId);
        var engine = new StubIntegrationEngine(provider);
        return new LeaderEconomicsService(engine, currentUser);
    }

    private sealed class StubAccountingProvider : IAccountingProvider
    {
        private readonly IReadOnlyList<AccountingDocument> _docs;
        public StubAccountingProvider(IReadOnlyList<AccountingDocument> docs) => _docs = docs;
        public string ProviderId => "mock";
        public string DisplayName => "Mock Accounting (Dev)";
        public Task<bool> TestConnectionAsync(string tenantId) => Task.FromResult(true);
        public Task<IEnumerable<AccountingDocument>> GetDocumentsForUserAsync(string tenantId, string userId, string startDate, string endDate) => Task.FromResult<IEnumerable<AccountingDocument>>(_docs);
        public Task<System.IO.Stream?> GetDocumentStreamAsync(string tenantId, string documentId) => Task.FromResult<System.IO.Stream?>(null);
        public Task<bool> SyncHoursAsync(string tenantId, object hoursData) => Task.FromResult(true);
    }

    private sealed class StubIntegrationEngine : IIntegrationEngine
    {
        private readonly IAccountingProvider _provider;
        public StubIntegrationEngine(IAccountingProvider provider) => _provider = provider;
        public Task<IAccountingProvider> GetAccountingProviderAsync(string tenantId) => Task.FromResult(_provider);
        public IEnumerable<IIntegrationProvider> GetAvailableProviders() => new[] { _provider };
    }

    private sealed class StubCurrentUserContext : ICurrentUserContext
    {
        public StubCurrentUserContext(Guid organizationId) => OrganizationId = organizationId;
        public Guid? UserId => Guid.NewGuid();
        public Guid? OrganizationId { get; }
        public string? Role => "Admin";
    }
}
