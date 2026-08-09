using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class AtomicJobStockPostingTests
{
    [Fact]
    public async Task Concurrent_conditional_consumptions_cannot_make_balance_negative()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"workslip-stock-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<SqlDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        try
        {
            await using (var setup = new SqlDbContext(options))
            {
                await setup.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE InventoryBalances (
                        OrganizationId TEXT NOT NULL,
                        MaterialId TEXT NOT NULL,
                        LocationId TEXT NOT NULL,
                        Quantity TEXT NOT NULL,
                        PRIMARY KEY (OrganizationId, MaterialId, LocationId));
                    """);
                var organizationId = Guid.NewGuid(); var materialId = Guid.NewGuid(); var locationId = Guid.NewGuid();
                await setup.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO InventoryBalances (OrganizationId, MaterialId, LocationId, Quantity)
                    VALUES ({organizationId}, {materialId}, {locationId}, {6m});
                    """);
            }

            async Task<int> ConsumeAsync()
            {
                await using var context = new SqlDbContext(options);
                await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;");
                return await context.InventoryBalances.Where(x => x.Quantity >= 4)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity - 4));
            }

            var results = await Task.WhenAll(ConsumeAsync(), ConsumeAsync());
            await using var verification = new SqlDbContext(options);
            Assert.Equal(new[] { 0, 1 }, results.OrderBy(x => x));
            Assert.Equal(2, await verification.InventoryBalances.Select(x => x.Quantity).SingleAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Retrying_submit_does_not_consume_stock_twice()
    {
        await using var database = await TestDatabase.CreateAsync();
        var data = await database.SeedAsync(stock: 10, requested: 4);
        var repository = CreateRepository(database.Context, data.UserId, data.OrganizationId);

        var first = await repository.TransitionAsync(data.JobId, data.OrganizationId, JobStatus.InReview, data.UserId, null, default);
        var retry = await repository.TransitionAsync(data.JobId, data.OrganizationId, JobStatus.InReview, data.UserId, null, default);

        Assert.True(first!.Changed);
        Assert.False(retry!.Changed);
        Assert.Equal(6, await database.Context.InventoryBalances.Select(x => x.Quantity).SingleAsync());
        Assert.Equal(4, await database.Context.JobMaterials.Select(x => x.PostedQuantity).SingleAsync());
        Assert.Single(await database.Context.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task Insufficient_stock_rolls_back_balances_movements_posted_quantity_and_status()
    {
        await using var database = await TestDatabase.CreateAsync();
        var data = await database.SeedAsync(stock: 3, requested: 4);
        var repository = CreateRepository(database.Context, data.UserId, data.OrganizationId);

        var exception = await Assert.ThrowsAsync<InventoryPostingException>(() =>
            repository.TransitionAsync(data.JobId, data.OrganizationId, JobStatus.InReview, data.UserId, null, default));
        Assert.Equal(InventoryPostingFailure.InsufficientStock, exception.Failure);

        database.Context.ChangeTracker.Clear();
        Assert.Equal(3, await database.Context.InventoryBalances.Select(x => x.Quantity).SingleAsync());
        Assert.Equal(0, await database.Context.JobMaterials.Select(x => x.PostedQuantity).SingleAsync());
        Assert.Empty(await database.Context.InventoryMovements.ToListAsync());
        Assert.Equal(JobStatus.Draft.ToString(), await database.Context.JobReports.Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Submit_snapshots_material_and_uses_one_posting_batch()
    {
        await using var database = await TestDatabase.CreateAsync();
        var data = await database.SeedAsync(stock: 10, requested: 2);
        var repository = CreateRepository(database.Context, data.UserId, data.OrganizationId);

        await repository.TransitionAsync(data.JobId, data.OrganizationId, JobStatus.InReview, data.UserId, null, default);

        var line = await database.Context.JobMaterials.SingleAsync();
        var movement = await database.Context.InventoryMovements.SingleAsync();
        Assert.Equal("Copper pipe", line.MaterialNameSnapshot);
        Assert.Equal("m", line.UnitSnapshot);
        Assert.Equal(12.50m, line.UnitCostSnapshot);
        Assert.Equal(line.PostingBatchId, movement.PostingBatchId);
        Assert.Equal(-2, movement.Quantity);
    }

    [Fact]
    public async Task Inactive_reference_rejects_submit_without_mutation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var data = await database.SeedAsync(stock: 10, requested: 2, materialActive: false);
        var repository = CreateRepository(database.Context, data.UserId, data.OrganizationId);

        var exception = await Assert.ThrowsAsync<InventoryPostingException>(() =>
            repository.TransitionAsync(data.JobId, data.OrganizationId, JobStatus.InReview, data.UserId, null, default));

        Assert.Equal(InventoryPostingFailure.InactiveOrForeignReference, exception.Failure);
        database.Context.ChangeTracker.Clear();
        Assert.Equal(10, await database.Context.InventoryBalances.Select(x => x.Quantity).SingleAsync());
        Assert.Equal(JobStatus.Draft.ToString(), await database.Context.JobReports.Select(x => x.Status).SingleAsync());
    }

    private static EfJobRepository CreateRepository(SqlDbContext context, Guid userId, Guid organizationId)
    {
        var retry = new NoRetryPolicy();
        var currentUser = new TestUser(userId, organizationId);
        var worksheets = new EfWorksheetRepository(context, currentUser, retry);
        var views = new EfJobViewRepository(NullLogger<EfJobViewRepository>.Instance, context);
        var assignments = new EfAssignmentRepository(context, retry, currentUser, worksheets, views);
        return new EfJobRepository(context, retry, new EfCustomerRepository(context, retry), assignments,
            new EfJobLinkRepository(context, retry), worksheets, views);
    }

    private sealed class TestDatabase(SqliteConnection connection, SqlDbContext context) : IAsyncDisposable
    {
        public SqlDbContext Context { get; } = context;
        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            connection.CreateFunction<string?, bool>("isjson", value => value is null || IsJson(value));
            connection.CreateFunction<string>("sysutcdatetime", () => DateTimeOffset.UtcNow.ToString("O"));
            var context = new SqlDbContext(new DbContextOptionsBuilder<SqlDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        private static bool IsJson(string value)
        {
            try { System.Text.Json.JsonDocument.Parse(value).Dispose(); return true; }
            catch (System.Text.Json.JsonException) { return false; }
        }

        public async Task<SeedData> SeedAsync(decimal stock, decimal requested, bool materialActive = true)
        {
            await Context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON;");
            var organizationId = Guid.NewGuid(); var userId = Guid.NewGuid(); var jobId = Guid.NewGuid();
            var materialId = Guid.NewGuid(); var locationId = Guid.NewGuid();
            Context.Organizations.Add(new OrganizationRow { Id = organizationId, Name = "Test", Cvr = "12345678" });
            Context.Users.Add(new UserDataRow { Id = userId, OrganizationId = organizationId, DisplayName = "Tester", Email = "test@example.com", Role = Roles.User });
            Context.JobReports.Add(new JobReportRow { Id = jobId, OrganizationId = organizationId, Status = JobStatus.Draft.ToString(), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            Context.InventoryMaterials.Add(new InventoryMaterialRow { Id = materialId, OrganizationId = organizationId, Name = "Copper pipe", Unit = "m", UnitCost = 12.50m, IsActive = materialActive });
            Context.InventoryLocations.Add(new InventoryLocationRow { Id = locationId, OrganizationId = organizationId, Name = "Van", IsActive = true });
            Context.InventoryBalances.Add(new InventoryBalanceRow { OrganizationId = organizationId, MaterialId = materialId, LocationId = locationId, Quantity = stock });
            Context.JobMaterials.Add(new JobMaterialRow { Id = Guid.NewGuid(), OrganizationId = organizationId, JobId = jobId, MaterialId = materialId, LocationId = locationId, Quantity = requested });
            await Context.SaveChangesAsync();
            await Context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF;");
            return new SeedData(organizationId, userId, jobId);
        }

        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed record SeedData(Guid OrganizationId, Guid UserId, Guid JobId);
    private sealed class TestUser(Guid userId, Guid organizationId) : ICurrentUserContext
    { public Guid? UserId => userId; public Guid? OrganizationId => organizationId; public string? Role => Roles.User; }
    private sealed class NoRetryPolicy : IDatabaseRetryPolicy
    {
        public Task ExecuteAsync(string name, Func<CancellationToken, Task> action, CancellationToken token) => action(token);
        public Task<T> ExecuteAsync<T>(string name, Func<CancellationToken, Task<T>> action, CancellationToken token) => action(token);
    }
}
