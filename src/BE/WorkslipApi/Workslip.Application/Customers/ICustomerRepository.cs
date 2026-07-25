namespace Workslip.Application.Customers;

public interface ICustomerRepository
{
    Task<Guid> CreateCustomerAsync(Guid organizationId, CustomerData customer, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerListItemResponse>> ListAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken);
    Task<int> GetCustomerCountAsync(Guid organizationId, string? search, CancellationToken cancellationToken);
    Task<CustomerDetailResponse?> GetByIdAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);
    Task UpdateAsync(Guid organizationId, Guid id, CustomerData customer, CancellationToken cancellationToken);
    Task SetTopAsync(Guid organizationId, Guid id, bool isTop, CancellationToken cancellationToken);
    Task DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerSearchResponse>> SearchAsync(Guid organizationId, string query, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerSearchResponse>> GetTopCustomersAsync(Guid organizationId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlySet<string>> GetExistingCustomerNumbersAsync(Guid organizationId, IReadOnlyCollection<string> customerNumbers, CancellationToken cancellationToken);
    Task<CustomerBulkCreateResult> BulkCreateAsync(Guid organizationId, IReadOnlyList<CustomerData> customers, CancellationToken cancellationToken);
}
