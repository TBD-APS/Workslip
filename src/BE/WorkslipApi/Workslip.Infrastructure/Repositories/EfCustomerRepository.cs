using System.Data;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Customers;
using Workslip.Application.Jobs;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfCustomerRepository : ICustomerRepository
{
    private readonly SqlDbContext _dbContext;

    public EfCustomerRepository(SqlDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Guid> UpsertByEmailAsync(
        Guid organizationId,
        CustomerInfo customer,
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Customers.FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.Email == customer.Email,cancellationToken);

        if (existing is not null)
        {
            var entry = _dbContext.Entry(existing);
            entry.Property(e => e.Name).CurrentValue = customer.Name ?? string.Empty;
            entry.Property(e => e.Address).CurrentValue = customer.Address;
            entry.Property(e => e.ContactPerson).CurrentValue = customer.ContactPerson;
            entry.Property(e => e.Phone).CurrentValue = customer.Phone;
            entry.Property(e => e.UpdatedAt).CurrentValue = DateTimeOffset.UtcNow;
        }
        else
        {
            var row = new CustomerRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = customer.Name ?? string.Empty,
                Address = customer.Address,
                Email = customer.Email,
                ContactPerson = customer.ContactPerson,
                Phone = customer.Phone,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.Customers.Add(row);
            existing = row;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return existing.Id;
    }
}
