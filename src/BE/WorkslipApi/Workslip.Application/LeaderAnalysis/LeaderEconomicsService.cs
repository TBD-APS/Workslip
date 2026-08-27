using Workslip.Application.Auth;
using Workslip.Application.Integrations;

namespace Workslip.Application.LeaderAnalysis;

public sealed class LeaderEconomicsService(
    IIntegrationEngine integrationEngine,
    ICurrentUserContext currentUser) : ILeaderEconomicsService
{
    public async Task<LeaderEconomicsSummaryResponse> GetSummaryAsync(string? startDate, string? endDate, CancellationToken cancellationToken)
    {
        var documents = await GetDocumentsInternalAsync(startDate, endDate, cancellationToken);
        var provider = await GetProviderAsync();

        var invoiceCount = documents.Count(d => d.Type.Equals("Invoice", StringComparison.OrdinalIgnoreCase));
        var receiptCount = documents.Count(d => d.Type.Equals("Receipt", StringComparison.OrdinalIgnoreCase));
        var totalAmount = documents.Sum(d => d.Amount);
        var averageAmount = documents.Count > 0 ? totalAmount / documents.Count : 0;

        // Keep most recent 5 by Date desc (Date is string, parse as DateTime if possible)
        var recent = documents
            .OrderByDescending(d => d.Date)
            .Take(5)
            .ToList();

        return new LeaderEconomicsSummaryResponse(
            provider.ProviderId,
            provider.DisplayName,
            documents.Count,
            invoiceCount,
            receiptCount,
            totalAmount,
            Math.Round(averageAmount, 2),
            recent);
    }

    public async Task<LeaderEconomicsResponse> GetDocumentsAsync(string? startDate, string? endDate, CancellationToken cancellationToken)
    {
        var documents = await GetDocumentsInternalAsync(startDate, endDate, cancellationToken);
        var provider = await GetProviderAsync();
        return new LeaderEconomicsResponse(provider.ProviderId, provider.DisplayName, documents);
    }

    private async Task<IAccountingProvider> GetProviderAsync()
    {
        if (currentUser.OrganizationId is not Guid organizationId)
            throw new UnauthorizedAccessException("Missing organization context.");

        return await integrationEngine.GetAccountingProviderAsync(organizationId.ToString());
    }

    private async Task<IReadOnlyList<AccountingDocument>> GetDocumentsInternalAsync(string? startDate, string? endDate, CancellationToken cancellationToken)
    {
        var provider = await GetProviderAsync();

        var organizationId = currentUser.OrganizationId!.Value.ToString();
        // Use synthetic userId for org-wide view; EconomicsProvider ignores userId, Mock uses it deterministically
        var userId = "leader-analysis";

        // Default to last 6 months if not provided
        var end = string.IsNullOrWhiteSpace(endDate) ? DateTime.UtcNow.ToString("yyyy-MM-dd") : endDate!;
        var start = string.IsNullOrWhiteSpace(startDate) ? DateTime.UtcNow.AddMonths(-6).ToString("yyyy-MM-dd") : startDate!;

        var docs = await provider.GetDocumentsForUserAsync(organizationId, userId, start, end);
        // Respect cancellation
        cancellationToken.ThrowIfCancellationRequested();
        return docs.OrderByDescending(d => d.Date).ToList();
    }
}
