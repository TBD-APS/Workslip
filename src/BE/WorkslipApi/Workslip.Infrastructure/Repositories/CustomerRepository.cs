using System.Data;
using Dapper;
using Workslip.Application.Customers;
using Workslip.Application.Jobs;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Repositories;

public sealed class CustomerRepository : ICustomerRepository
{
    public async Task<Guid> UpsertByEmailAsync(Guid organizationId, CustomerInfo customer, IDbConnection connection, IDbTransaction transaction, CancellationToken cancellationToken)
    {
        var customerId = await connection.QuerySingleAsync<Guid>(new CommandDefinition(
            """
            merge dbo.Customers with (holdlock) as target
            using (select @OrganizationId as OrganizationId, @Email as Email) as source
            on target.OrganizationId = source.OrganizationId and target.Email = source.Email
            when matched then
                update set
                    Name = @Name,
                    Address = @Address,
                    ContactPerson = @ContactPerson,
                    Phone = @Phone,
                    UpdatedAt = sysutcdatetime()
            when not matched then
                insert (Id, OrganizationId, Name, Address, Email, ContactPerson, Phone, CreatedAt, UpdatedAt)
                values (NEWID(), @OrganizationId, @Name, @Address, @Email, @ContactPerson, @Phone, sysutcdatetime(), sysutcdatetime())
            output inserted.Id;
            """,
            new
            {
                OrganizationId = organizationId,
                Name = customer.Name ?? string.Empty,
                customer.Address,
                Email = customer.Email!,
                customer.ContactPerson,
                customer.Phone
            },
            transaction,
            cancellationToken: cancellationToken));

        return customerId;
    }
}
