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

    public async Task<Guid> CreateCustomerAsync(Guid organizationId, CustomerInfo customer, CancellationToken cancellationToken)
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

    public async Task<IReadOnlyList<CustomerSearchResponse>> SearchAsync(Guid organizationId, string query, int limit, CancellationToken cancellationToken)
    {
        var trimmed = query.Trim();

        var customers = await _dbContext.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .Where(c =>
                (c.Name != null && c.Name.Contains(trimmed)) ||
                (c.Email != null && c.Email.Contains(trimmed)) ||
                (c.Phone != null && c.Phone.Contains(trimmed)) ||
                (c.Address != null && c.Address.Contains(trimmed)))
            .OrderBy(c => c.Name != null && c.Name.StartsWith(trimmed) ? 0 : 1)
            .ThenBy(c => c.Name)
            .Take(limit)
            .Select(c => new CustomerSearchResponse(
                c.Id,
                c.Name,
                c.Email,
                c.Phone,
                c.Address,
                c.ContactPerson))
            .ToListAsync(cancellationToken);

        return customers ?? new List<CustomerSearchResponse>();
    }

    public async Task<IReadOnlyList<CustomerSearchResponse>> GetTopCustomersAsync(Guid organizationId, int limit, CancellationToken cancellationToken)
    {
        var customers = await _dbContext.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .OrderByDescending(c => _dbContext.JobReports.Count(r => r.CustomerId == c.Id && r.OrganizationId == organizationId && !r.IsSoftDeleted))
            .Take(limit)
            .Select(c => new CustomerSearchResponse(
                c.Id,
                c.Name,
                c.Email,
                c.Phone,
                c.Address,
                c.ContactPerson))
            .ToListAsync(cancellationToken);

        return customers;
    }

    public async Task UpdateAsync(Guid organizationId, Guid id, CustomerInfo customer, CancellationToken cancellationToken)
    {
        var row = await _dbContext.Customers
            .FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id, cancellationToken);

        if (row is null)
        {
            return;
        }

        _dbContext.Entry(row).Property(x => x.Name).CurrentValue = customer.Name ?? string.Empty;
        _dbContext.Entry(row).Property(x => x.Address).CurrentValue = customer.Address;
        _dbContext.Entry(row).Property(x => x.Email).CurrentValue = customer.Email;
        _dbContext.Entry(row).Property(x => x.ContactPerson).CurrentValue = customer.ContactPerson;
        _dbContext.Entry(row).Property(x => x.Phone).CurrentValue = customer.Phone;
        _dbContext.Entry(row).Property(x => x.UpdatedAt).CurrentValue = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        var row = await _dbContext.Customers
            .FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id, cancellationToken);

        if (row is null)
        {
            return;
        }

        var linkedJobs = await _dbContext.JobReports
            .Where(j => j.OrganizationId == organizationId && j.CustomerId == id)
            .ToListAsync(cancellationToken);

        foreach (var job in linkedJobs)
        {
            _dbContext.Entry(job).Property(e => e.CustomerId).CurrentValue = null;
        }

        _dbContext.Customers.Remove(row);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
