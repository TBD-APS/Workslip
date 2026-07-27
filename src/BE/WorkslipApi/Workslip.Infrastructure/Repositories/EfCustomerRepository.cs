using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Customers;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfCustomerRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy) : ICustomerRepository
{
    private const string CustomerNumberIndexName = "UX_Customers_Organization_CustomerNumber";

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

    public Task<IReadOnlyList<CustomerSearchResponse>> GetFavoriteCustomersAsync(Guid organizationId, int limit, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.favorite", token => GetFavoriteCustomersCoreAsync(organizationId, limit, token), cancellationToken);

    public Task UpdateAsync(Guid organizationId, Guid id, CustomerData customer, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.update", token => UpdateCoreAsync(organizationId, id, customer, token), cancellationToken);

    public Task SetFavoriteAsync(Guid organizationId, Guid id, bool isFavorite, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.set-favorite", token => SetFavoriteCoreAsync(organizationId, id, isFavorite, token), cancellationToken);

    public Task DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.delete", token => DeleteCoreAsync(organizationId, id, token), cancellationToken);

    public Task<IReadOnlySet<string>> GetExistingCustomerNumbersAsync(Guid organizationId, IReadOnlyCollection<string> customerNumbers, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.existing-numbers", token => GetExistingCustomerNumbersCoreAsync(organizationId, customerNumbers, token), cancellationToken);

    public Task<CustomerBulkCreateResult> BulkCreateAsync(Guid organizationId, IReadOnlyList<CustomerData> customers, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("customers.bulk-create", token => BulkCreateCoreAsync(organizationId, customers, token), cancellationToken);

    private async Task<Guid> CreateCustomerCoreAsync(Guid organizationId, CustomerData customer, CancellationToken cancellationToken)
    {
        var row = ToRow(organizationId, customer, DateTimeOffset.UtcNow);
        dbContext.Customers.Add(row);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (customer.CustomerNumber is not null && IsCustomerNumberConflict(exception))
        {
            dbContext.ChangeTracker.Clear();
            throw CreateConflictException(customer.CustomerNumber);
        }

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
                c.IsFavorite,
                JobCount = dbContext.JobReports.Count(r => r.OrganizationId == organizationId && r.CustomerId == c.Id && !r.IsSoftDeleted)
            })
            .ToListAsync(cancellationToken);

        return customers.Select(c => new CustomerListItemResponse(
            c.Id, c.CustomerNumber, c.Name, c.Address, c.ZipCode, c.City, c.Country,
            c.Email, c.ContactPerson, c.Phone, c.JobCount, c.IsFavorite)).ToArray();
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
                c.IsFavorite,
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
            customer.IsFavorite,
            customer.Jobs.Select(j => new CustomerJobResponse(
                j.Id, j.ReportNumber, j.Status, j.UpdatedAt, customer.ContactPerson, customer.Phone)).ToArray());
    }

    private async Task<IReadOnlyList<CustomerSearchResponse>> SearchCoreAsync(Guid organizationId, string query, int limit, CancellationToken cancellationToken)
    {
        var term = query.Trim();
        var customers = ApplySearch(
            dbContext.Customers.AsNoTracking().Where(c => c.OrganizationId == organizationId),
            term);

        return await customers
            .OrderBy(c => c.IsFavorite ? 0 : 1)
            .ThenBy(c => c.Name.StartsWith(term) ? 0 : 1)
            .ThenBy(c => c.Name)
            .Take(limit)
            .Select(c => new CustomerSearchResponse(
                c.Id, c.CustomerNumber, c.Name, c.Email, c.Phone, c.Address,
                c.ZipCode, c.City, c.Country, c.ContactPerson, c.IsFavorite))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CustomerSearchResponse>> GetFavoriteCustomersCoreAsync(Guid organizationId, int limit, CancellationToken cancellationToken)
    {
        var favoriteCustomers = await dbContext.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId && c.IsFavorite)
            .OrderBy(c => c.Name)
            .Take(limit)
            .Select(c => new CustomerSearchResponse(
                c.Id, c.CustomerNumber, c.Name, c.Email, c.Phone, c.Address,
                c.ZipCode, c.City, c.Country, c.ContactPerson, c.IsFavorite))
            .ToListAsync(cancellationToken);

        if (favoriteCustomers.Count > 0)
            return favoriteCustomers;

        return await dbContext.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .Select(c => new CustomerSearchResponse(
                c.Id, c.CustomerNumber, c.Name, c.Email, c.Phone, c.Address,
                c.ZipCode, c.City, c.Country, c.ContactPerson, c.IsFavorite))
            .ToListAsync(cancellationToken);
    }

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

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (customer.CustomerNumber is not null && IsCustomerNumberConflict(exception))
        {
            dbContext.ChangeTracker.Clear();
            throw CreateConflictException(customer.CustomerNumber);
        }
    }

    private async Task SetFavoriteCoreAsync(Guid organizationId, Guid id, bool isFavorite, CancellationToken cancellationToken)
    {
        var row = await dbContext.Customers.FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id, cancellationToken);
        if (row is null)
        {
            return;
        }

        dbContext.Entry(row).Property(x => x.IsFavorite).CurrentValue = isFavorite;
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

    private async Task<CustomerBulkCreateResult> BulkCreateCoreAsync(Guid organizationId, IReadOnlyList<CustomerData> customers, CancellationToken cancellationToken)
    {
        var pending = customers.ToList();
        var conflictingNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            var rows = pending.Select(customer => ToRow(organizationId, customer, now)).ToArray();
            dbContext.Customers.AddRange(rows);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return new CustomerBulkCreateResult(rows.Length, conflictingNumbers);
            }
            catch (DbUpdateException exception) when (IsCustomerNumberConflict(exception))
            {
                dbContext.ChangeTracker.Clear();
                var candidateNumbers = pending
                    .Select(customer => customer.CustomerNumber)
                    .Where(number => number is not null)
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var existingNumbers = await GetExistingCustomerNumbersCoreAsync(
                    organizationId,
                    candidateNumbers,
                    cancellationToken);
                var newlyConflicting = existingNumbers
                    .Where(conflictingNumbers.Add)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (newlyConflicting.Count == 0)
                {
                    throw;
                }

                pending = pending
                    .Where(customer => customer.CustomerNumber is null || !existingNumbers.Contains(customer.CustomerNumber))
                    .ToList();
            }
        }

        return new CustomerBulkCreateResult(0, conflictingNumbers);
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

    private static bool IsCustomerNumberConflict(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        sqlException.Number is 2601 or 2627 &&
        sqlException.Message.Contains(CustomerNumberIndexName, StringComparison.OrdinalIgnoreCase);

    private static CustomerNumberConflictException CreateConflictException(string customerNumber) =>
        new(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { customerNumber });
}
