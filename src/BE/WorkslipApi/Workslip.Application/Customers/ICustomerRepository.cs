using Workslip.Application.Jobs;

namespace Workslip.Application.Customers;

public interface ICustomerRepository
{
    Task<Guid> UpsertCustomerAsync(Guid organizationId, CustomerInfo customer, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerListItemResponse>> ListAsync(Guid organizationId, int limit, int offset, CancellationToken cancellationToken);

    Task<CustomerDetailResponse?> GetByIdAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);
}
