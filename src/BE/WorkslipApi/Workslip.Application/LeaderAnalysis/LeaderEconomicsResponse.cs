using Workslip.Application.Integrations;

namespace Workslip.Application.LeaderAnalysis;

public sealed record LeaderEconomicsSummaryResponse(
    string ProviderId,
    string ProviderDisplayName,
    int DocumentCount,
    int InvoiceCount,
    int ReceiptCount,
    decimal TotalAmount,
    decimal AverageAmount,
    IReadOnlyList<AccountingDocument> RecentDocuments);

public sealed record LeaderEconomicsResponse(
    string ProviderId,
    string ProviderDisplayName,
    IReadOnlyList<AccountingDocument> Documents);
