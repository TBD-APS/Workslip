using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Customers;

public sealed class EfCustomerRepositorySortingTests
{
    [Fact]
    public async Task ListAsync_ImplementsClickableCustomerSortContract()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite(connection)
            .Options;

        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        var alpha = CreateCustomer(organizationId, "Alpha", "A vej", "20000000");
        var beta = CreateCustomer(organizationId, "Beta", "B vej", "10000000");
        var gamma = CreateCustomer(organizationId, "Gamma", "A vej", "20000000");
        var empty = CreateCustomer(organizationId, "Empty", null, null);

        await using (var setupContext = new SqlDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Organizations.AddRange(
                new OrganizationRow { Id = organizationId, Name = "Org", Cvr = "12345678" },
                new OrganizationRow { Id = otherOrganizationId, Name = "Other", Cvr = "87654321" });
            setupContext.Customers.AddRange(
                alpha,
                beta,
                gamma,
                empty,
                CreateCustomer(otherOrganizationId, "Other tenant", "0 vej", "00000000"));
            setupContext.JobReports.AddRange(
                CreateJob(organizationId, alpha, "A-1"),
                CreateJob(organizationId, alpha, "A-2"),
                CreateJob(organizationId, beta, "B-1"));
            await setupContext.SaveChangesAsync();
        }

        await using var context = new SqlDbContext(options);
        var repository = new EfCustomerRepository(context, new NoRetryPolicy());

        await AssertOrder(repository, organizationId, "address", "asc", "Alpha", "Gamma", "Beta", "Empty");
        await AssertOrder(repository, organizationId, "address", "desc", "Beta", "Alpha", "Gamma", "Empty");
        await AssertOrder(repository, organizationId, "phone", "asc", "Beta", "Alpha", "Gamma", "Empty");
        await AssertOrder(repository, organizationId, "phone", "desc", "Alpha", "Gamma", "Beta", "Empty");
        await AssertOrder(repository, organizationId, "jobCount", "asc", "Empty", "Gamma", "Beta", "Alpha");
        await AssertOrder(repository, organizationId, "jobCount", "desc", "Alpha", "Beta", "Empty", "Gamma");
    }

    private static async Task AssertOrder(
        EfCustomerRepository repository,
        Guid organizationId,
        string sortBy,
        string sortDirection,
        params string[] expectedNames)
    {
        var result = await repository.ListAsync(
            organizationId,
            limit: 20,
            offset: 0,
            search: null,
            sortBy,
            sortDirection,
            CancellationToken.None);

        Assert.Equal(expectedNames, result.Select(customer => customer.Name));
    }

    private static CustomerRow CreateCustomer(Guid organizationId, string name, string? address, string? phone)
    {
        var now = DateTimeOffset.UtcNow;
        return new CustomerRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Address = address,
            Phone = phone,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static JobReportRow CreateJob(Guid organizationId, CustomerRow customer, string reportNumber)
    {
        var now = DateTimeOffset.UtcNow;
        return new JobReportRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            ReportNumber = reportNumber,
            Status = "Draft",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private sealed class NoRetryPolicy : IDatabaseRetryPolicy
    {
        public Task ExecuteAsync(
            string operationName,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);

        public Task<T> ExecuteAsync<T>(
            string operationName,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
    }
}
