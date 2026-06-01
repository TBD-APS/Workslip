using System.Data;
using Workslip.Application.Jobs;

namespace Workslip.Application.Customers;

public interface ICustomerRepository
{
    Task<Guid> UpsertCustomerAsync(Guid organizationId, CustomerInfo customer, CancellationToken cancellationToken);
}
