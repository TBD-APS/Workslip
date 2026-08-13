using Workslip.Application.Users;

namespace Workslip.Infrastructure.Repositories;

internal sealed class HistorySafeUserBillingRepository(SqlUserBillingRepository inner) : IUserBillingRepository
{
    public Task<decimal?> GetRateAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        inner.GetRateAsync(organizationId, userId, cancellationToken);

    public Task SetRateAsync(Guid organizationId, Guid userId, decimal? rate, CancellationToken cancellationToken) =>
        inner.SetRateAsync(organizationId, userId, rate, cancellationToken);
}
