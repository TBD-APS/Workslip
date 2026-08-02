using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Notifications;

public sealed class PushSubscriptionOwnershipTests
{
    [Fact]
    public async Task RegisterSubscriptionAsync_MovesEndpointToCurrentUser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite(connection)
            .Options;
        var arneId = Guid.NewGuid();
        var nielsId = Guid.NewGuid();
        const string endpoint = "https://push.example/device";

        await using (var setupContext = new SqlDbContext(options))
        {
            await setupContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE PushSubscriptions (
                    Id TEXT NOT NULL PRIMARY KEY,
                    UserId TEXT NOT NULL,
                    Endpoint TEXT NOT NULL UNIQUE,
                    P256Dh TEXT NOT NULL,
                    Auth TEXT NOT NULL,
                    UserAgent TEXT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedUtc TEXT NOT NULL,
                    LastSeenUtc TEXT NOT NULL
                );
                """);
            setupContext.PushSubscriptions.Add(new PushSubscriptionRow
            {
                Id = Guid.NewGuid(),
                UserId = arneId,
                Endpoint = endpoint,
                P256Dh = "arne-key",
                Auth = "arne-auth",
                UserAgent = "arne-agent",
                IsActive = true,
                CreatedUtc = DateTimeOffset.UtcNow.AddDays(-1),
                LastSeenUtc = DateTimeOffset.UtcNow.AddDays(-1)
            });
            await setupContext.SaveChangesAsync();
        }

        await using (var context = new SqlDbContext(options))
        {
            var repository = new EfNotificationRepository(context, new NoRetryPolicy());
            await repository.RegisterSubscriptionAsync(
                nielsId,
                endpoint,
                "niels-key",
                "niels-auth",
                "niels-agent",
                replacedEndpoint: null,
                CancellationToken.None);
        }

        await using var assertionContext = new SqlDbContext(options);
        var subscription = await assertionContext.PushSubscriptions
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(nielsId, subscription.UserId);
        Assert.Equal("niels-key", subscription.P256Dh);
        Assert.Equal("niels-auth", subscription.Auth);
        Assert.Equal("niels-agent", subscription.UserAgent);
        Assert.True(subscription.IsActive);
    }

    [Fact]
    public void Model_RequiresGloballyUniqueEndpoint()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var context = new SqlDbContext(options);
        var entity = context.Model.FindEntityType(typeof(PushSubscriptionRow));

        var endpointIndex = Assert.Single(
            entity!.GetIndexes().Where(index =>
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(PushSubscriptionRow.Endpoint)])));

        Assert.True(endpointIndex.IsUnique);
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
