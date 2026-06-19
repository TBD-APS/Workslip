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

    public async Task<Result<CustomerDetailResponse>> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            logger.LogWarning("Customer update requested without OrganizationId in claims.");
            return Result<CustomerDetailResponse>.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name?.Trim()))
        {
            return Result<CustomerDetailResponse>.Conflict("Customer name is required.");
        }

        var existing = await customerRepository.GetByIdAsync(organizationId.Value, id, cancellationToken);
        if (existing is null)
        {
            return Result<CustomerDetailResponse>.NotFound();
        }

        if (existing.JobCount > 0)
        {
            logger.LogInformation("Updating customer {CustomerId} which has {JobCount} jobs — update allowed for masterdata", id, existing.JobCount);
        }

        var updatedCustomer = new CustomerInfo(
            request.Name!.Trim(),
            request.Address?.Trim(),
            request.Email?.Trim(),
            request.ContactPerson?.Trim(),
            request.Phone?.Trim());

        await customerRepository.UpdateAsync(organizationId.Value, id, updatedCustomer, cancellationToken);

        var updated = await customerRepository.GetByIdAsync(organizationId.Value, id, cancellationToken);
        return Result<CustomerDetailResponse>.Success(updated!);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            logger.LogWarning("Customer delete requested without OrganizationId in claims.");
            return Result.Unauthorized();
        }

        var existing = await customerRepository.GetByIdAsync(organizationId.Value, id, cancellationToken);
        if (existing is null)
        {
            return Result.NotFound();
        }

        if (existing.JobCount > 0)
        {
            logger.LogWarning("Attempted to delete customer {CustomerId} which has {JobCount} jobs — blocked", id, existing.JobCount);
            return Result.Conflict("Cannot delete customer with associated jobs.");
        }

        await customerRepository.DeleteAsync(organizationId.Value, id, cancellationToken);
        logger.LogInformation("Customer {CustomerId} deleted successfully", id);
        return Result.Ok();
    }
}
