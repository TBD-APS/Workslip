using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Workslip.Tests.Infrastructure;

namespace Workslip.Tests.Notifications;

public sealed class NotificationHistoryVisibilityTests
{
    [Fact]
    public async Task GetHistoryAsync_hides_future_notifications_until_they_are_due()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(SqliteSchemaCompatibilityInterceptor.Instance)
            .Options;
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var setupContext = new SqlDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            var organizationId = Guid.NewGuid();
            setupContext.Organizations.Add(new OrganizationRow
            {
                Id = organizationId,
                Name = "Notification visibility test",
                Cvr = "12345678",
                CreatedAt = now,
                UpdatedAt = now
            });
            setupContext.Users.Add(new UserDataRow
            {
                Id = userId,
                OrganizationId = organizationId,
                Email = $"{userId:N}@example.test",
                DisplayName = "Notification test user",
                EntraId = $"entra-{userId:N}",
                EntraEmail = $"{userId:N}@example.test",
                Role = "User",
                CreatedAt = now,
                UpdatedAt = now
            });
            setupContext.NotificationQueue.AddRange(
                CreateNotification(userId, now.AddMinutes(-1), NotificationType.DailyHoursLimitReached),
                CreateNotification(userId, now.AddHours(1), NotificationType.ConversationReminder));
            await setupContext.SaveChangesAsync();
        }

        await using var context = new SqlDbContext(options);
        var repository = new EfNotificationRepository(context, new NoRetryPolicy());

        var history = await repository.GetHistoryAsync(userId, 20, 0, CancellationToken.None);

        var visible = Assert.Single(history);
        Assert.Equal(NotificationType.DailyHoursLimitReached.ToString(), visible.NotificationType);
    }

    private static NotificationQueueRow CreateNotification(
        Guid userId,
        DateTimeOffset nextAttemptUtc,
        NotificationType type) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        NotificationType = type.ToString(),
        PayloadJson = "{}",
        Status = "Pending",
        RetryCount = 0,
        CreatedUtc = DateTimeOffset.UtcNow,
        NextAttemptUtc = nextAttemptUtc
    };

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
