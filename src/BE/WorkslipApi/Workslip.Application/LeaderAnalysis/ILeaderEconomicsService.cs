namespace Workslip.Application.LeaderAnalysis;

public interface ILeaderEconomicsService
{
    Task<LeaderEconomicsSummaryResponse> GetSummaryAsync(string? startDate, string? endDate, CancellationToken cancellationToken);
    Task<LeaderEconomicsResponse> GetDocumentsAsync(string? startDate, string? endDate, CancellationToken cancellationToken);
}
