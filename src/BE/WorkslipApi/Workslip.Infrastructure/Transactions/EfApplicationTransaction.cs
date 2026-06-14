using Microsoft.EntityFrameworkCore.Storage;
using Workslip.Application.Common;

namespace Workslip.Infrastructure.Transactions;

public sealed class EfApplicationTransaction(IDbContextTransaction transaction) : IApplicationTransaction
{
    public Task CommitAsync(CancellationToken cancellationToken) =>
        transaction.CommitAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken) =>
        transaction.RollbackAsync(cancellationToken);

    public ValueTask DisposeAsync() =>
        transaction.DisposeAsync();
}
