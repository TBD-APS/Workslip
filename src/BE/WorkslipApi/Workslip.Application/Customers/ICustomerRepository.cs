using Workslip.Application.Jobs;

namespace Workslip.Application.Customers;

public interface ICustomerRepository
{
    Task<Guid> CreateCustomerAsync(Guid organizationId, CustomerInfo customer, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerListItemResponse>> ListAsync(Guid organizationId, int limit, int offset, CancellationToken cancellationToken);

    Task<CustomerDetailResponse?> GetByIdAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    Task UpdateAsync(Guid organizationId, Guid id, CustomerInfo customer, CancellationToken cancellationToken);
    Task DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerSearchResponse>> SearchAsync(Guid organizationId, string query, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerSearchResponse>> GetTopCustomersAsync(Guid organizationId, int limit, CancellationToken cancellationToken);
}
