using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Application.Common;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Workslip.Tests.Infrastructure;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class EfReferenceDataRepositoryTests
{
    [Fact]
    public async Task GetAsync_OrdersInstallationTypesAlphabeticallyInsteadOfBySortOrder()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddHybridCache();
        using var services = serviceCollection.BuildServiceProvider();
        var organizationId = Guid.NewGuid();

        database.Context.Organizations.Add(CreateOrganization(organizationId));
        database.Context.InstallationTypeDefinitions.AddRange(
            CreateInstallationType(organizationId, "Ventilation", 1),
            CreateInstallationType(organizationId, "Brugsvand", 2),
            CreateInstallationType(organizationId, "Afløb", 3));
        await database.Context.SaveChangesAsync();

        var repository = new EfReferenceDataRepository(
            database.Context,
            new NoRetryPolicy(),
            services.GetRequiredService<HybridCache>(),
            new CacheDiagnostics(
            [
                new CacheRegionDefinition(CacheRegionNames.ReferenceData, "HybridCache", 600)
            ]));

        var result = await repository.GetAsync(organizationId, CancellationToken.None);

        Assert.Equal(
            ["Afløb", "Brugsvand", "Ventilation"],
            result.InstallationTypes.Select(type => type.Name).ToArray());
        Assert.Equal(
            [3, 2, 1],
            result.InstallationTypes.Select(type => type.SortOrder).ToArray());
    }

    private static InstallationTypeDefinitionRow CreateInstallationType(
        Guid organizationId,
        string name,
        int sortOrder) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Name = name,
        SortOrder = sortOrder
    };

    private static OrganizationRow CreateOrganization(Guid id) => new()
    {
        Id = id,
        Name = "Reference data test organization",
        Cvr = "12345678",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class NoRetryPolicy : IDatabaseRetryPolicy
    {
        public Task ExecuteAsync(
            string operationName,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public Task<T> ExecuteAsync<T>(
            string operationName,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }

    private sealed class RelationalTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private RelationalTestDatabase(SqliteConnection connection, SqlDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        internal SqlDbContext Context { get; }

        internal static async Task<RelationalTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.CreateFunction(
                "sysutcdatetime",
                () => DateTimeOffset.UtcNow.ToString("O"),
                isDeterministic: false);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<SqlDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(SqliteSchemaCompatibilityInterceptor.Instance)
                .Options;
            var context = new SqlDbContext(options);
            await context.Database.EnsureCreatedAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON;");

            return new RelationalTestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
