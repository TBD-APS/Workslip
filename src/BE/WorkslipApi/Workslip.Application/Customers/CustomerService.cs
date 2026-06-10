using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;

namespace Workslip.Application.Customers;

public sealed class CustomerService(
    ICustomerRepository customerRepository,
    ICurrentUserContext currentUser,
    ILogger<CustomerService> logger) : ICustomerService
{
    public async Task<Result<IReadOnlyList<CustomerListItemResponse>>> ListAsync(CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            logger.LogWarning("Customer list requested without OrganizationId in claims.");
            return Result<IReadOnlyList<CustomerListItemResponse>>.Unauthorized();
        }

        var customers = await customerRepository.ListAsync(organizationId.Value, cancellationToken);
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
}
