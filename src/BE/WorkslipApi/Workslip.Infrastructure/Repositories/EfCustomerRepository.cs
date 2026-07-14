using System.Data;
using Microsoft.EntityFrameworkCore;
using Workslip.Application;
using Workslip.Application.Customers;
using Workslip.Application.Jobs;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfCustomerRepository : ICustomerRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;

    public EfCustomerRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
    }

    public Task<Guid> CreateCustomerAsync(Guid organizationId, CustomerInfo customer, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("customers.create", token => CreateCustomerCoreAsync(organizationId, customer, token), cancellationToken);

    public Task<IReadOnlyList<CustomerListItemResponse>> ListAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("customers.list", token => ListCoreAsync(organizationId, limit, offset, search, sortBy, sortDirection, token), cancellationToken);

    public Task<int> GetCustomerCountAsync(Guid organizationId, string? search, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("customers.count", token => GetCustomerCountCoreAsync(organizationId, search, token), cancellationToken);

    public Task<CustomerDetailResponse?> GetByIdAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("customers.get-by-id", token => GetByIdCoreAsync(organizationId, id, token), cancellationToken);

    public Task<IReadOnlyList<CustomerSearchResponse>> SearchAsync(Guid organizationId, string query, int limit, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("customers.search", token => SearchCoreAsync(organizationId, query, limit, token), cancellationToken);

    public Task<IReadOnlyList<CustomerSearchResponse>> GetTopCustomersAsync(Guid organizationId, int limit, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("customers.top", token => GetTopCustomersCoreAsync(organizationId, limit, token), cancellationToken);

    public Task UpdateAsync(Guid organizationId, Guid id, CustomerInfo customer, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("customers.update", token => UpdateCoreAsync(organizationId, id, customer, token), cancellationToken);

    public Task DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("customers.delete", token => DeleteCoreAsync(organizationId, id, token), cancellationToken);

    public Task<int> BulkCreateAsync(Guid organizationId, IReadOnlyList<CustomerInfo> customers, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("customers.bulk-create", token => BulkCreateCoreAsync(organizationId, customers, token), cancellationToken);

    private async Task<Guid> CreateCustomerCoreAsync(Guid organizationId, CustomerInfo customer, CancellationToken cancellationToken)
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

    private async Task<IReadOnlyList<CustomerListItemResponse>> ListCoreAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken)
    {
        var query = _dbContext.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c =>
                (c.Name != null && c.Name.Contains(term)) ||
                (c.Email != null && c.Email.Contains(term)) ||
                (c.Address != null && c.Address.Contains(term)) ||
                (c.ContactPerson != null && c.ContactPerson.Contains(term)) ||
                (c.Phone != null && c.Phone.Contains(term)));
        }

        query = (sortBy, sortDirection) switch
        {
            ("name", "asc") => query.OrderBy(c => c.Name),
            ("name", "desc") => query.OrderByDescending(c => c.Name),
            ("address", "asc") => query.OrderBy(c => c.Address),
            ("address", "desc") => query.OrderByDescending(c => c.Address),
            ("email", "asc") => query.OrderBy(c => c.Email),
            ("email", "desc") => query.OrderByDescending(c => c.Email),
            ("contactPerson", "asc") => query.OrderBy(c => c.ContactPerson),
            ("contactPerson", "desc") => query.OrderByDescending(c => c.ContactPerson),
            _ => query.OrderByDescending(c => _dbContext.JobReports.Count(r =>
                r.OrganizationId == organizationId && r.CustomerId == c.Id && !r.IsSoftDeleted))
                .ThenBy(c => c.Name)
        };

        var customers = await query
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

    private async Task<int> GetCustomerCountCoreAsync(Guid organizationId, string? search, CancellationToken cancellationToken)
    {
        var query = _dbContext.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c =>
                (c.Name != null && c.Name.Contains(term)) ||
                (c.Email != null && c.Email.Contains(term)) ||
                (c.Address != null && c.Address.Contains(term)) ||
                (c.ContactPerson != null && c.ContactPerson.Contains(term)) ||
                (c.Phone != null && c.Phone.Contains(term)));
        }

        return await query.CountAsync(cancellationToken);
    }

    private async Task<CustomerDetailResponse?> GetByIdCoreAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
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

    private async Task<IReadOnlyList<CustomerSearchResponse>> SearchCoreAsync(Guid organizationId, string query, int limit, CancellationToken cancellationToken)
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

    private async Task<IReadOnlyList<CustomerSearchResponse>> GetTopCustomersCoreAsync(Guid organizationId, int limit, CancellationToken cancellationToken)
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

    private async Task UpdateCoreAsync(Guid organizationId, Guid id, CustomerInfo customer, CancellationToken cancellationToken)
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

    private async Task DeleteCoreAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
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

    private async Task<int> BulkCreateCoreAsync(Guid organizationId, IReadOnlyList<CustomerInfo> customers, CancellationToken cancellationToken)
    {
        const int batchSize = 500;
        var now = DateTimeOffset.UtcNow;
        var totalCount = 0;

        for (var i = 0; i < customers.Count; i += batchSize)
        {
            var batch = customers.Skip(i).Take(batchSize);
            var rows = batch.Select(c => new CustomerRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = c.Name ?? string.Empty,
                Address = c.Address,
                Email = c.Email,
                ContactPerson = c.ContactPerson,
                Phone = c.Phone,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList();

            _dbContext.Customers.AddRange(rows);
            await _dbContext.SaveChangesAsync(cancellationToken);
            totalCount += rows.Count;
        }

        return totalCount;
    }
}
