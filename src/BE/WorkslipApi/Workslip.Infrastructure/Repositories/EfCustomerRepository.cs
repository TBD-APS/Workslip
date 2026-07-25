using Microsoft.EntityFrameworkCore;
using Workslip.Application.Customers;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfCustomerRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy) : ICustomerRepository
{
    public Task<Guid> CreateCustomerAsync(Guid organizationId, CustomerData customer, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.create", token => CreateCustomerCoreAsync(organizationId, customer, token), cancellationToken);

    public Task<IReadOnlyList<CustomerListItemResponse>> ListAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.list", token => ListCoreAsync(organizationId, limit, offset, search, sortBy, sortDirection, token), cancellationToken);

    public Task<int> GetCustomerCountAsync(Guid organizationId, string? search, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.count", token => GetCustomerCountCoreAsync(organizationId, search, token), cancellationToken);

    public Task<CustomerDetailResponse?> GetByIdAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.get-by-id", token => GetByIdCoreAsync(organizationId, id, token), cancellationToken);

    public Task<IReadOnlyList<CustomerSearchResponse>> SearchAsync(Guid organizationId, string query, int limit, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.search", token => SearchCoreAsync(organizationId, query, limit, token), cancellationToken);

    public Task<IReadOnlyList<CustomerSearchResponse>> GetTopCustomersAsync(Guid organizationId, int limit, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.top", token => GetTopCustomersCoreAsync(organizationId, limit, token), cancellationToken);

    public Task UpdateAsync(Guid organizationId, Guid id, CustomerData customer, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.update", token => UpdateCoreAsync(organizationId, id, customer, token), cancellationToken);

    public Task SetTopAsync(Guid organizationId, Guid id, bool isTop, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.set-top", token => SetTopCoreAsync(organizationId, id, isTop, token), cancellationToken);

    public Task DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.delete", token => DeleteCoreAsync(organizationId, id, token), cancellationToken);

    public Task<IReadOnlySet<string>> GetExistingCustomerNumbersAsync(Guid organizationId, IReadOnlyCollection<string> customerNumbers, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.existing-numbers", token => GetExistingCustomerNumbersCoreAsync(organizationId, customerNumbers, token), cancellationToken);

    public Task<int> BulkCreateAsync(Guid organizationId, IReadOnlyList<CustomerData> customers, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.bulk-create", token => BulkCreateCoreAsync(organizationId, customers, token), cancellationToken);

    private async Task<Guid> CreateCustomerCoreAsync(Guid organizationId, CustomerData customer, CancellationToken cancellationToken)
    {
        var row = ToRow(organizationId, customer, DateTimeOffset.UtcNow);
        dbContext.Customers.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    private async Task<IReadOnlyList<CustomerListItemResponse>> ListCoreAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken)
    {
        var query = ApplySearch(dbContext.Customers.AsNoTracking().Where(c => c.OrganizationId == organizationId), search);

        query = (sortBy, sortDirection) switch
        {
            ("name", "asc") => query.OrderBy(c => c.Name),
            ("name", "desc") => query.OrderByDescending(c => c.Name),
            ("customerNumber", "asc") => query.OrderBy(c => c.CustomerNumber),
            ("customerNumber", "desc") => query.OrderByDescending(c => c.CustomerNumber),
            ("address", "asc") => query.OrderBy(c => c.Address),
            ("address", "desc") => query.OrderByDescending(c => c.Address),
            ("email", "asc") => query.OrderBy(c => c.Email),
            ("email", "desc") => query.OrderByDescending(c => c.Email),
            ("contactPerson", "asc") => query.OrderBy(c => c.ContactPerson),
            ("contactPerson", "desc") => query.OrderByDescending(c => c.ContactPerson),
            _ => query.OrderByDescending(c => dbContext.JobReports.Count(r =>
                    r.OrganizationId == organizationId && r.CustomerId == c.Id && !r.IsSoftDeleted))
                .ThenBy(c => c.Name)
        };

        var customers = await query
            .Skip(offset)
            .Take(limit)
            .Select(c => new
            {
                c.Id,
                c.CustomerNumber,
                c.Name,
                c.Address,
                c.ZipCode,
                c.City,
                c.Country,
                c.Email,
                c.ContactPerson,
                c.Phone,
                c.IsTop,
                JobCount = dbContext.JobReports.Count(r => r.OrganizationId == organizationId && r.CustomerId == c.Id && !r.IsSoftDeleted)
            })
            .ToListAsync(cancellationToken);

        return customers.Select(c => new CustomerListItemResponse(
            c.Id, c.CustomerNumber, c.Name, c.Address, c.ZipCode, c.City, c.Country,
            c.Email, c.ContactPerson, c.Phone, c.JobCount, c.IsTop)).ToArray();
    }

    private Task<int> GetCustomerCountCoreAsync(Guid organizationId, string? search, CancellationToken cancellationToken) =>
        ApplySearch(dbContext.Customers.AsNoTracking().Where(c => c.OrganizationId == organizationId), search)
            .CountAsync(cancellationToken);

    private async Task<CustomerDetailResponse?> GetByIdCoreAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.Id == id)
            .Select(c => new
            {
                c.Id,
                c.CustomerNumber,
                c.Name,
                c.Address,
                c.ZipCode,
                c.City,
                c.Country,
                c.Email,
                c.ContactPerson,
                c.Phone,
                JobCount = dbContext.JobReports.Count(r => r.OrganizationId == organizationId && r.CustomerId == c.Id && !r.IsSoftDeleted),
                Jobs = dbContext.JobReports
                    .Where(r => r.OrganizationId == organizationId && r.CustomerId == c.Id && !r.IsSoftDeleted)
                    .OrderByDescending(r => r.UpdatedAt)
                    .Select(r => new { r.Id, r.ReportNumber, r.Status, r.UpdatedAt })
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return null;
        }

        return new CustomerDetailResponse(
            customer.Id,
            customer.CustomerNumber,
            customer.Name,
            customer.Address,
            customer.ZipCode,
            customer.City,
            customer.Country,
            customer.Email,
            customer.ContactPerson,
            customer.Phone,
            customer.JobCount,
            customer.Jobs.Select(j => new CustomerJobResponse(
                j.Id, j.ReportNumber, j.Status, j.UpdatedAt, customer.ContactPerson, customer.Phone)).ToArray());
    }

    private async Task<IReadOnlyList<CustomerSearchResponse>> SearchCoreAsync(Guid organizationId, string query, int limit, CancellationToken cancellationToken)
    {
        var term = query.Trim();
        return await dbContext.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .Where(c =>
                (c.CustomerNumber != null && c.CustomerNumber.Contains(term)) ||
                c.Name.Contains(term) ||
                (c.Email != null && c.Email.Contains(term)) ||
                (c.Phone != null && c.Phone.Contains(term)) ||
                (c.Address != null && c.Address.Contains(term)) ||
                (c.ZipCode != null && c.ZipCode.Contains(term)) ||
                (c.City != null && c.City.Contains(term)))
            .OrderBy(c => c.IsTop ? 0 : 1)
            .ThenBy(c => c.Name.StartsWith(term) ? 0 : 1)
            .ThenBy(c => c.Name)
            .Take(limit)
            .Select(c => new CustomerSearchResponse(
                c.Id, c.CustomerNumber, c.Name, c.Email, c.Phone, c.Address,
                c.ZipCode, c.City, c.Country, c.ContactPerson, c.IsTop))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CustomerSearchResponse>> GetTopCustomersCoreAsync(Guid organizationId, int limit, CancellationToken cancellationToken) =>
        await dbContext.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.IsTop)
            .OrderBy(c => c.Name)
            .Take(limit)
            .Select(c => new CustomerSearchResponse(
                c.Id, c.CustomerNumber, c.Name, c.Email, c.Phone, c.Address,
                c.ZipCode, c.City, c.Country, c.ContactPerson, c.IsTop))
            .ToListAsync(cancellationToken);

    private async Task UpdateCoreAsync(Guid organizationId, Guid id, CustomerData customer, CancellationToken cancellationToken)
    {
        var row = await dbContext.Customers.FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id, cancellationToken);
        if (row is null)
        {
            return;
        }

        dbContext.Entry(row).Property(x => x.CustomerNumber).CurrentValue = customer.CustomerNumber;
        dbContext.Entry(row).Property(x => x.Name).CurrentValue = customer.Name;
        dbContext.Entry(row).Property(x => x.Address).CurrentValue = customer.Address;
        dbContext.Entry(row).Property(x => x.ZipCode).CurrentValue = customer.ZipCode;
        dbContext.Entry(row).Property(x => x.City).CurrentValue = customer.City;
        dbContext.Entry(row).Property(x => x.Country).CurrentValue = customer.Country;
        dbContext.Entry(row).Property(x => x.Email).CurrentValue = customer.Email;
        dbContext.Entry(row).Property(x => x.ContactPerson).CurrentValue = customer.ContactPerson;
        dbContext.Entry(row).Property(x => x.Phone).CurrentValue = customer.Phone;
        dbContext.Entry(row).Property(x => x.UpdatedAt).CurrentValue = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SetTopCoreAsync(Guid organizationId, Guid id, bool isTop, CancellationToken cancellationToken)
    {
        var row = await dbContext.Customers.FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id, cancellationToken);
        if (row is null)
        {
            return;
        }

        dbContext.Entry(row).Property(x => x.IsTop).CurrentValue = isTop;
        dbContext.Entry(row).Property(x => x.UpdatedAt).CurrentValue = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task DeleteCoreAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        var row = await dbContext.Customers.FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id, cancellationToken);
        if (row is null)
        {
            return;
        }

        var linkedJobs = await dbContext.JobReports
            .Where(j => j.OrganizationId == organizationId && j.CustomerId == id)
            .ToListAsync(cancellationToken);
        foreach (var job in linkedJobs)
        {
            dbContext.Entry(job).Property(e => e.CustomerId).CurrentValue = null;
        }

        dbContext.Customers.Remove(row);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlySet<string>> GetExistingCustomerNumbersCoreAsync(Guid organizationId, IReadOnlyCollection<string> customerNumbers, CancellationToken cancellationToken)
    {
        if (customerNumbers.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var matches = await dbContext.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.CustomerNumber != null && customerNumbers.Contains(c.CustomerNumber))
            .Select(c => c.CustomerNumber!)
            .ToListAsync(cancellationToken);
        return matches.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<int> BulkCreateCoreAsync(Guid organizationId, IReadOnlyList<CustomerData> customers, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = customers.Select(customer => ToRow(organizationId, customer, now)).ToArray();
        dbContext.Customers.AddRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);
        return rows.Length;
    }

    private static CustomerRow ToRow(Guid organizationId, CustomerData customer, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        CustomerNumber = customer.CustomerNumber,
        Name = customer.Name,
        Address = customer.Address,
        ZipCode = customer.ZipCode,
        City = customer.City,
        Country = customer.Country,
        Email = customer.Email,
        ContactPerson = customer.ContactPerson,
        Phone = customer.Phone,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static IQueryable<CustomerRow> ApplySearch(IQueryable<CustomerRow> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var term = search.Trim();
        return query.Where(c =>
            (c.CustomerNumber != null && c.CustomerNumber.Contains(term)) ||
            c.Name.Contains(term) ||
            (c.Email != null && c.Email.Contains(term)) ||
            (c.Address != null && c.Address.Contains(term)) ||
            (c.ZipCode != null && c.ZipCode.Contains(term)) ||
            (c.City != null && c.City.Contains(term)) ||
            (c.ContactPerson != null && c.ContactPerson.Contains(term)) ||
            (c.Phone != null && c.Phone.Contains(term)));
    }
}
