using Workslip.Application.Common;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Transactions;

public sealed class EfApplicationTransactionFactory(SqlDbContext dbContext) : IApplicationTransactionFactory
{
    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        return new EfApplicationTransaction(transaction);
    }
}
