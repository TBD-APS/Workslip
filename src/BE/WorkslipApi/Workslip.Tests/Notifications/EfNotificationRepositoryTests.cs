using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Notifications;

public sealed class EfNotificationRepositoryTests
{
    [Fact]
    public async Task QueueNotificationAsync_DetachesRowAfterPersistenceFailure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite(connection)
            .Options;
        var notificationId = Guid.NewGuid();

        await using (var setupContext = new SqlDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.NotificationQueue.Add(CreateNotification(notificationId));
            await setupContext.SaveChangesAsync();
        }

        await using var context = new SqlDbContext(options);
        var duplicate = CreateNotification(notificationId);
        var repository = new EfNotificationRepository(context, new NoRetryPolicy());

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            repository.QueueNotificationAsync(duplicate, CancellationToken.None));

        Assert.Equal(EntityState.Detached, context.Entry(duplicate).State);
    }

    [Fact]
    public async Task RegisterSubscriptionAsync_DeactivatesReplacedEndpoint()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite(connection)
            .Options;
        var userId = Guid.NewGuid();
        const string oldEndpoint = "https://push.example/old";
        const string newEndpoint = "https://push.example/new";

        await using (var setupContext = new SqlDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.PushSubscriptions.Add(new PushSubscriptionRow
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Endpoint = oldEndpoint,
                P256Dh = "old-key",
                Auth = "old-auth",
                IsActive = true,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastSeenUtc = DateTimeOffset.UtcNow
            });
            await setupContext.SaveChangesAsync();
        }

        await using (var context = new SqlDbContext(options))
        {
            var repository = new EfNotificationRepository(context, new NoRetryPolicy());
            await repository.RegisterSubscriptionAsync(
                userId,
                newEndpoint,
                "new-key",
                "new-auth",
                "test-agent",
                oldEndpoint,
                CancellationToken.None);
        }

        await using var assertionContext = new SqlDbContext(options);
        var subscriptions = await assertionContext.PushSubscriptions
            .AsNoTracking()
            .OrderBy(subscription => subscription.Endpoint)
            .ToListAsync();

        Assert.Equal(2, subscriptions.Count);
        Assert.False(subscriptions.Single(subscription => subscription.Endpoint == oldEndpoint).IsActive);
        Assert.True(subscriptions.Single(subscription => subscription.Endpoint == newEndpoint).IsActive);
    }

    private static NotificationQueueRow CreateNotification(Guid id) => new()
    {
        Id = id,
        UserId = Guid.NewGuid(),
        NotificationType = NotificationType.JobDeleted.ToString(),
        PayloadJson = "{}",
        Status = "Pending",
        RetryCount = 0,
        CreatedUtc = DateTimeOffset.UtcNow,
        NextAttemptUtc = DateTimeOffset.UtcNow
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
}
