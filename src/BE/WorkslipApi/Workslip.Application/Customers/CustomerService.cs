using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;

namespace Workslip.Application.Customers;

public sealed class CustomerService(
    ICustomerRepository customerRepository,
    ICurrentUserContext currentUser,
    IValidator<CreateCustomerRequest> createValidator,
    IValidator<UpdateCustomerRequest> updateValidator,
    ILogger<CustomerService> logger) : ICustomerService
{
    public async Task<Result<CustomerListResponse>> ListAsync(int? limit, int? offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            logger.LogWarning("Customer list requested without OrganizationId in claims.");
            return Result<CustomerListResponse>.Unauthorized();
        }

        var normalizedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var normalizedOffset = Math.Max(offset ?? 0, 0);
        var customers = await customerRepository.ListAsync(organizationId.Value, normalizedLimit, normalizedOffset, search, sortBy, sortDirection, cancellationToken);
        var totalCount = await customerRepository.GetCustomerCountAsync(organizationId.Value, search, cancellationToken);
        return Result<CustomerListResponse>.Success(new CustomerListResponse(customers, totalCount));
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

    public async Task<Result<CustomerDetailResponse>> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            logger.LogWarning("Customer create requested without OrganizationId in claims.");
            return Result<CustomerDetailResponse>.Unauthorized();
        }

        var validationResult = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .Select(e => new ValidationError { Identifier = e.PropertyName, ErrorMessage = e.ErrorMessage })
                .ToList();
            return Result<CustomerDetailResponse>.Invalid(errors);
        }

        var customerInfo = new CustomerInfo(
            Guid.Empty,
            request.Name!.Trim(),
            request.Address?.Trim(),
            request.Email?.Trim(),
            request.ContactPerson?.Trim(),
            request.Phone?.Trim());

        logger.LogInformation("Creating customer {CustomerName} in org {OrgId}", customerInfo.Name, organizationId);

        var id = await customerRepository.CreateCustomerAsync(organizationId.Value, customerInfo, cancellationToken);

        var created = await customerRepository.GetByIdAsync(organizationId.Value, id, cancellationToken);
        return Result<CustomerDetailResponse>.Success(created!);
    }

    public async Task<Result<CustomerDetailResponse>> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            logger.LogWarning("Customer update requested without OrganizationId in claims.");
            return Result<CustomerDetailResponse>.Unauthorized();
        }

        var validationResult = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .Select(e => new ValidationError { Identifier = e.PropertyName, ErrorMessage = e.ErrorMessage })
                .ToList();
            return Result<CustomerDetailResponse>.Invalid(errors);
        }

        var existing = await customerRepository.GetByIdAsync(organizationId.Value, id, cancellationToken);
        if (existing is null)
        {
            return Result<CustomerDetailResponse>.NotFound();
        }

        var updatedCustomer = new CustomerInfo(
            id,
            request.Name!.Trim(),
            request.Address?.Trim(),
            request.Email?.Trim(),
            request.ContactPerson?.Trim(),
            request.Phone?.Trim());

        logger.LogInformation("Updating customer {CustomerId} with new values: {@UpdatedCustomer} in org {OrgId}", id, updatedCustomer, organizationId);

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

        logger.LogInformation("Customer {CustomerId} is about to be deleted in org {OrgId}", id, organizationId);
        await customerRepository.DeleteAsync(organizationId.Value, id, cancellationToken);
        logger.LogInformation("Customer {CustomerId} deleted successfully in org {OrgId}", id, organizationId);
        return Result.Success();
    }
}
