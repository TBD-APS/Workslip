using Ardalis.Result;
using Workslip.Application.Jobs;

namespace Workslip.Application.Customers;

public interface ICustomerService
{
    Task<Result<CustomerListResponse>> ListAsync(int? limit, int? offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken);
    Task<Result<CustomerDetailResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<CustomerSearchResponse>>> SearchAsync(string? query, int? limit, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<CustomerSearchResponse>>> GetTopAsync(int limit, CancellationToken cancellationToken);
    Task<Result<CustomerDetailResponse>> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken);
    Task<Result<CustomerDetailResponse>> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken);
    Task<Result> SetTopAsync(Guid id, bool isTop, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<ImportCustomerResponse>> ImportAsync(IReadOnlyList<CustomerInfo> customers, CancellationToken cancellationToken);
}
