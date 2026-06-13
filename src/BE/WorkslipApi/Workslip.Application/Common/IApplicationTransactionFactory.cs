namespace Workslip.Application.Common;

public interface IApplicationTransactionFactory
{
    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
