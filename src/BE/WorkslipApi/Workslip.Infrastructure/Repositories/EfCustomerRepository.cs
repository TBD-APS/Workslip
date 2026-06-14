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

    public async Task<Guid> UpsertCustomerAsync(Guid organizationId, CustomerInfo customer, CancellationToken cancellationToken)
    {
        CustomerRow? existing = null;

        if (customer.CustomerId is not null)
        {
            existing = await _dbContext.Customers
                .FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == customer.CustomerId.Value, cancellationToken);
        }

        if (existing is null && customer.Email is not null)
        {
            existing = await _dbContext.Customers
                .FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.Email == customer.Email, cancellationToken);
        }

        if (existing is not null)
        {
            var entry = _dbContext.Entry(existing);
            entry.Property(e => e.Name).CurrentValue = customer.Name ?? string.Empty;
            entry.Property(e => e.Address).CurrentValue = customer.Address;
            entry.Property(e => e.ContactPerson).CurrentValue = customer.ContactPerson;
            entry.Property(e => e.Phone).CurrentValue = customer.Phone;
            entry.Property(e => e.UpdatedAt).CurrentValue = DateTimeOffset.UtcNow;
            return existing.Id;
        }

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
        await _dbContext.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<IReadOnlyList<CustomerListItemResponse>> ListAsync(Guid organizationId, int limit, int offset, CancellationToken cancellationToken)
    {
        var customers = await _dbContext.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Address,
                c.Email,
                c.ContactPerson,
                c.Phone,
                JobCount = _dbContext.JobReports
                    .Count(r => r.OrganizationId == organizationId
                                && r.CustomerId == c.Id
                                && !r.IsSoftDeleted)
            })
            .OrderByDescending(c => c.JobCount)
            .ThenBy(c => c.Name)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return customers
            .Select(c => new CustomerListItemResponse(
                c.Id,
                c.Name,
                c.Address,
                c.Email,
                c.ContactPerson,
                c.Phone,
                c.JobCount))
            .ToArray();
    }

    public async Task<CustomerDetailResponse?> GetByIdAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        var customer = await _dbContext.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.Id == id)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Address,
                c.Email,
                c.ContactPerson,
                c.Phone,
                JobCount = _dbContext.JobReports
                    .Count(r => r.OrganizationId == organizationId
                                && r.CustomerId == c.Id
                                && !r.IsSoftDeleted),
                Jobs = _dbContext.JobReports
                    .Where(r => r.OrganizationId == organizationId
                                && r.CustomerId == c.Id
                                && !r.IsSoftDeleted)
                    .OrderByDescending(r => r.UpdatedAt)
                    .Select(r => new
                    {
                        r.Id,
                        r.ReportNumber,
                        r.Status,
                        r.UpdatedAt,
                        ContactPerson = c.ContactPerson,
                        ContactPhone = c.Phone
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return null;
        }

        return new CustomerDetailResponse(
            customer.Id,
            customer.Name,
            customer.Address,
            customer.Email,
            customer.ContactPerson,
            customer.Phone,
            customer.JobCount,
            customer.Jobs
                .Select(j => new CustomerJobResponse(
                    j.Id,
                    j.ReportNumber,
                    j.Status,
                    j.UpdatedAt,
                    j.ContactPerson,
                    j.ContactPhone))
                .ToArray());
    }
}
