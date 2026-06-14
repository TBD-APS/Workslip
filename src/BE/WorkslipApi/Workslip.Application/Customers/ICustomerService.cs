using Ardalis.Result;

namespace Workslip.Application.Customers;

public interface ICustomerService
{
    Task<Result<IReadOnlyList<CustomerListItemResponse>>> ListAsync(int? limit, int? offset, CancellationToken cancellationToken);
    Task<Result<CustomerDetailResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
