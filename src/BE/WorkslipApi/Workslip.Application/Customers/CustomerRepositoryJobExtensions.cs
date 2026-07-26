using Workslip.Application.Jobs;

namespace Workslip.Application.Customers;

public static class CustomerRepositoryJobExtensions
{
    public static Task<Guid> CreateCustomerAsync(
        this ICustomerRepository repository,
        Guid organizationId,
        CustomerInfo customer,
        CancellationToken cancellationToken) =>
        repository.CreateCustomerAsync(organizationId, new CustomerData(
            null,
            customer.Name?.Trim() ?? string.Empty,
            customer.Address?.Trim(),
            null,
            null,
            null,
            customer.Email?.Trim(),
            customer.ContactPerson?.Trim(),
            customer.Phone?.Trim()), cancellationToken);
}
