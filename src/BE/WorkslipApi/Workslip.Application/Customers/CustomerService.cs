using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;

namespace Workslip.Application.Customers;

public sealed class CustomerService(
    ICustomerRepository customerRepository,
    ICurrentUserContext currentUser,
    ILogger<CustomerService> logger) : ICustomerService
{
    public async Task<Result<IReadOnlyList<CustomerListItemResponse>>> ListAsync(int? limit, int? offset, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            logger.LogWarning("Customer list requested without OrganizationId in claims.");
            return Result<IReadOnlyList<CustomerListItemResponse>>.Unauthorized();
        }

        var normalizedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var normalizedOffset = Math.Max(offset ?? 0, 0);
        var customers = await customerRepository.ListAsync(organizationId.Value, normalizedLimit, normalizedOffset, cancellationToken);
        return Result<IReadOnlyList<CustomerListItemResponse>>.Success(customers);
    }

    public async Task<Result<CustomerDetailResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            logger.LogWarning("Customer detail requested without OrganizationId in claims.");
            return Result<CustomerDetailResponse>.Unauthorized();
        }

        var customer = await customerRepository.GetByIdAsync(organizationId.Value, id, cancellationToken);
        if (customer is null)
        {
            return Result<CustomerDetailResponse>.NotFound();
        }

        return Result<CustomerDetailResponse>.Success(customer);
    }

    public async Task<Result<IReadOnlyList<CustomerSearchResponse>>> SearchAsync(string? query, int? limit, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            logger.LogWarning("Customer search requested without OrganizationId in claims.");
            return Result<IReadOnlyList<CustomerSearchResponse>>.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return Result<IReadOnlyList<CustomerSearchResponse>>.Success(Array.Empty<CustomerSearchResponse>());
        }

        var normalizedLimit = Math.Clamp(limit ?? 10, 1, 25);
        var customers = await customerRepository.SearchAsync(organizationId.Value, query, normalizedLimit, cancellationToken);
        return Result<IReadOnlyList<CustomerSearchResponse>>.Success(customers);
    }

    public async Task<Result<IReadOnlyList<CustomerSearchResponse>>> GetTopAsync(int limit, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            logger.LogWarning("Top customers requested without OrganizationId in claims.");
            return Result<IReadOnlyList<CustomerSearchResponse>>.Unauthorized();
        }

        var normalizedLimit = Math.Clamp(limit, 1, 25);
        var customers = await customerRepository.GetTopCustomersAsync(organizationId.Value, normalizedLimit, cancellationToken);
        return Result<IReadOnlyList<CustomerSearchResponse>>.Success(customers);
    }
}
