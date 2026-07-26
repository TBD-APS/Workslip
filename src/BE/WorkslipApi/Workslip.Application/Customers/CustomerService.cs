using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;

namespace Workslip.Application.Customers;

public sealed class CustomerService(
    ICustomerRepository customerRepository,
    ICurrentUserContext currentUser,
    IValidator<CreateCustomerRequest> createValidator,
    IValidator<UpdateCustomerRequest> updateValidator,
    ILogger<CustomerService> logger) : ICustomerService
{
    private const int MaxImportRows = 10_000;

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
        return customer is null
            ? Result<CustomerDetailResponse>.NotFound()
            : Result<CustomerDetailResponse>.Success(customer);
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

    public async Task<Result<IReadOnlyList<CustomerSearchResponse>>> GetFavoriteAsync(int limit, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            logger.LogWarning("Favorite customers requested without OrganizationId in claims.");
            return Result<IReadOnlyList<CustomerSearchResponse>>.Unauthorized();
        }

        var normalizedLimit = Math.Clamp(limit, 1, 25);
        var customers = await customerRepository.GetFavoriteCustomersAsync(organizationId.Value, normalizedLimit, cancellationToken);
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
            return Result<CustomerDetailResponse>.Invalid(validationResult.Errors
                .Select(e => new ValidationError { Identifier = e.PropertyName, ErrorMessage = e.ErrorMessage })
                .ToList());
        }

        var customer = ToCustomerData(request);
        logger.LogInformation("Creating customer {CustomerName} in org {OrgId}", customer.Name, organizationId);

        try
        {
            var id = await customerRepository.CreateCustomerAsync(organizationId.Value, customer, cancellationToken);
            var created = await customerRepository.GetByIdAsync(organizationId.Value, id, cancellationToken);
            return Result<CustomerDetailResponse>.Success(created!);
        }
        catch (CustomerNumberConflictException)
        {
            logger.LogWarning("Customer create conflict. CustomerNumber: {CustomerNumber}. OrgId: {OrgId}", customer.CustomerNumber, organizationId);
            return Result<CustomerDetailResponse>.Conflict("customer_number_exists");
        }
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
            return Result<CustomerDetailResponse>.Invalid(validationResult.Errors
                .Select(e => new ValidationError { Identifier = e.PropertyName, ErrorMessage = e.ErrorMessage })
                .ToList());
        }

        var existing = await customerRepository.GetByIdAsync(organizationId.Value, id, cancellationToken);
        if (existing is null)
        {
            return Result<CustomerDetailResponse>.NotFound();
        }

        var customer = ToCustomerData(request);
        logger.LogInformation("Updating customer {CustomerId} in org {OrgId}", id, organizationId);

        try
        {
            await customerRepository.UpdateAsync(organizationId.Value, id, customer, cancellationToken);
            var updated = await customerRepository.GetByIdAsync(organizationId.Value, id, cancellationToken);
            return Result<CustomerDetailResponse>.Success(updated!);
        }
        catch (CustomerNumberConflictException)
        {
            logger.LogWarning("Customer update conflict. CustomerId: {CustomerId}. CustomerNumber: {CustomerNumber}. OrgId: {OrgId}", id, customer.CustomerNumber, organizationId);
            return Result<CustomerDetailResponse>.Conflict("customer_number_exists");
        }
    }

    public async Task<Result> SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            logger.LogWarning("Customer SetFavorite requested without OrganizationId in claims.");
            return Result.Unauthorized();
        }

        var existing = await customerRepository.GetByIdAsync(organizationId.Value, id, cancellationToken);
        if (existing is null)
        {
            return Result.NotFound();
        }

        await customerRepository.SetFavoriteAsync(organizationId.Value, id, isFavorite, cancellationToken);
        return Result.Success();
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

        await customerRepository.DeleteAsync(organizationId.Value, id, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ImportCustomerResponse>> ImportAsync(IReadOnlyList<ImportCustomerRow> customers, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            logger.LogWarning("Customer import requested without OrganizationId in claims.");
            return Result<ImportCustomerResponse>.Unauthorized();
        }

        if (customers.Count > MaxImportRows)
        {
            return Result<ImportCustomerResponse>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "File", ErrorMessage = $"For mange rækker. Maksimum er {MaxImportRows}." }
            });
        }

        if (customers.Count == 0)
        {
            return Result<ImportCustomerResponse>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "File", ErrorMessage = "Filen indeholder ingen kunder, der kan importeres." }
            });
        }

        var numbers = customers
            .Select(x => Clean(x.CustomerNumber))
            .Where(x => x is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var existingNumbers = await customerRepository.GetExistingCustomerNumbersAsync(organizationId.Value, numbers, cancellationToken);

        var seenNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validCustomers = new List<CustomerData>();
        var errors = new List<ImportCustomerError>();
        var duplicates = 0;

        foreach (var row in customers)
        {
            var request = new CreateCustomerRequest(
                Clean(row.Name) ?? string.Empty,
                Clean(row.CustomerNumber),
                Clean(row.Address),
                Clean(row.ZipCode),
                Clean(row.City),
                "Danmark",
                Clean(row.Email?.ToLowerInvariant()),
                Clean(row.ContactPerson),
                Clean(row.Phone));

            var validationResult = await createValidator.ValidateAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                errors.AddRange(validationResult.Errors.Select(error =>
                    new ImportCustomerError(row.RowNumber, error.PropertyName, error.ErrorMessage)));
                continue;
            }

            if (request.CustomerNumber is not null &&
                (!seenNumbers.Add(request.CustomerNumber) || existingNumbers.Contains(request.CustomerNumber)))
            {
                duplicates++;
                continue;
            }

            validCustomers.Add(ToCustomerData(request));
        }

        var bulkResult = validCustomers.Count == 0
            ? new CustomerBulkCreateResult(0, new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            : await customerRepository.BulkCreateAsync(organizationId.Value, validCustomers, cancellationToken);
        duplicates += bulkResult.ConflictingCustomerNumbers.Count;

        var failed = errors.Select(x => x.RowNumber).Distinct().Count();
        logger.LogInformation(
            "Customer import completed for org {OrgId}: {Imported} imported, {Duplicates} duplicates, {Failed} failed",
            organizationId, bulkResult.Imported, duplicates, failed);

        return Result<ImportCustomerResponse>.Success(new ImportCustomerResponse(
            bulkResult.Imported,
            duplicates,
            0,
            failed,
            errors));
    }

    private static CustomerData ToCustomerData(CreateCustomerRequest request) => new(
        Clean(request.CustomerNumber),
        request.Name.Trim(),
        Clean(request.Address),
        Clean(request.ZipCode),
        Clean(request.City),
        "Danmark",
        Clean(request.Email?.ToLowerInvariant()),
        Clean(request.ContactPerson),
        Clean(request.Phone));

    private static CustomerData ToCustomerData(UpdateCustomerRequest request) => new(
        Clean(request.CustomerNumber),
        request.Name.Trim(),
        Clean(request.Address),
        Clean(request.ZipCode),
        Clean(request.City),
        "Danmark",
        Clean(request.Email?.ToLower()),
        Clean(request.ContactPerson),
        Clean(request.Phone));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
