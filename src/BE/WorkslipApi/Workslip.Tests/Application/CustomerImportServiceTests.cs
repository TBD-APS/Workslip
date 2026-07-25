using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Customers;
using Workslip.Application.Customers.Validators;
using Xunit;

namespace Workslip.Tests.Application;

public sealed class CustomerImportServiceTests
{
    [Fact]
    public async Task Import_skips_existing_and_file_duplicate_customer_numbers()
    {
        var repository = new FakeCustomerRepository(["200"]);
        var service = new CustomerService(
            repository,
            new FakeCurrentUserContext(),
            new CreateCustomerRequestValidator(),
            new UpdateCustomerRequestValidator(),
            NullLogger<CustomerService>.Instance);
        var rows = new[]
        {
            Row(2, "100", "First"),
            Row(3, "100", "Duplicate in file"),
            Row(4, "200", "Existing"),
            Row(5, null, "Without number")
        };

        var result = await service.ImportAsync(rows, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Imported);
        Assert.Equal(2, result.Value.Duplicates);
        Assert.Equal(0, result.Value.Failed);
        Assert.Collection(repository.Imported,
            customer => Assert.Equal("100", customer.CustomerNumber),
            customer => Assert.Null(customer.CustomerNumber));
    }

    private static ImportCustomerRow Row(int rowNumber, string? number, string name) =>
        new(rowNumber, number, name, null, null, null, null, null, null, null);

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? OrganizationId { get; } = Guid.NewGuid();
        public string? Role { get; } = "Admin";
    }

    private sealed class FakeCustomerRepository(IEnumerable<string> existingNumbers) : ICustomerRepository
    {
        private readonly HashSet<string> _existingNumbers = existingNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<CustomerData> Imported { get; private set; } = [];

        public Task<IReadOnlySet<string>> GetExistingCustomerNumbersAsync(Guid organizationId, IReadOnlyCollection<string> customerNumbers, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(customerNumbers.Where(_existingNumbers.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase));

        public Task<int> BulkCreateAsync(Guid organizationId, IReadOnlyList<CustomerData> customers, CancellationToken cancellationToken)
        {
            Imported = customers;
            return Task.FromResult(customers.Count);
        }

        public Task<Guid> CreateCustomerAsync(Guid organizationId, CustomerData customer, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<CustomerListItemResponse>> ListAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<int> GetCustomerCountAsync(Guid organizationId, string? search, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<CustomerDetailResponse?> GetByIdAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task UpdateAsync(Guid organizationId, Guid id, CustomerData customer, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task SetTopAsync(Guid organizationId, Guid id, bool isTop, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<CustomerSearchResponse>> SearchAsync(Guid organizationId, string query, int limit, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<CustomerSearchResponse>> GetTopCustomersAsync(Guid organizationId, int limit, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
