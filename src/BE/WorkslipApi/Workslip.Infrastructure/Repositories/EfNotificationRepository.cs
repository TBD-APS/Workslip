using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Notifications;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfNotificationRepository : INotificationRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;

    public EfNotificationRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
    }

    public async Task QueueNotificationAsync(NotificationQueueRow row, CancellationToken cancellationToken)
    {
        await _retryPolicy.ExecuteAsync("notifications.queue", async token =>
        {
            _dbContext.NotificationQueue.Add(row);
            await _dbContext.SaveChangesAsync(token);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationQueueRow>> ClaimPendingNotificationsAsync(int batchSize, CancellationToken cancellationToken)
    {
        return await _retryPolicy.ExecuteAsync("notifications.claim", async token =>
        {
            var sql = @"
                UPDATE TOP (@BatchSize) NotificationQueue
                SET Status = 'Processing',
                    ProcessingStartedUtc = SYSUTCDATETIME()
                OUTPUT inserted.Id, inserted.UserId, inserted.NotificationType, inserted.PayloadJson, inserted.Status, inserted.RetryCount, inserted.CreatedUtc, inserted.ProcessingStartedUtc, inserted.NextAttemptUtc, inserted.CompletedUtc, inserted.LastError
                WHERE Status = 'Pending'
                  AND NextAttemptUtc <= SYSUTCDATETIME()";

            var connection = _dbContext.Database.GetDbConnection();
            var wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed)
            {
                await connection.OpenAsync(token);
            }
            try
            {
                var result = await connection.QueryAsync<NotificationQueueRow>(sql, new { BatchSize = batchSize });
                return (IReadOnlyList<NotificationQueueRow>)result.ToList();
            }
            finally
            {
                if (wasClosed)
                {
                    await connection.CloseAsync();
                }
            }
        }, cancellationToken);
    }

    public async Task UpdateNotificationStatusAsync(Guid id, string status, int retryCount, DateTimeOffset nextAttemptUtc, string? lastError, CancellationToken cancellationToken)
    {
        await _retryPolicy.ExecuteAsync("notifications.update_status", async token =>
        {
            var row = await _dbContext.NotificationQueue.FindAsync(new object[] { id }, token);
            if (row != null)
            {
                row.Status = status;
                row.RetryCount = retryCount;
                row.NextAttemptUtc = nextAttemptUtc;
                row.LastError = lastError;
                row.ProcessingStartedUtc = null;
                await _dbContext.SaveChangesAsync(token);
            }
        }, cancellationToken);
    }

    public async Task MarkNotificationCompletedAsync(Guid id, CancellationToken cancellationToken)
    {
        await _retryPolicy.ExecuteAsync("notifications.mark_completed", async token =>
        {
            var row = await _dbContext.NotificationQueue.FindAsync(new object[] { id }, token);
            if (row != null)
            {
                row.Status = "Completed";
                row.CompletedUtc = DateTimeOffset.UtcNow;
                row.ProcessingStartedUtc = null;
                await _dbContext.SaveChangesAsync(token);
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<PushSubscriptionRow>> GetActiveSubscriptionsForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _retryPolicy.ExecuteAsync("notifications.get_subscriptions", async token =>
        {
            var result = await _dbContext.PushSubscriptions
                .AsNoTracking()
                .Where(s => s.UserId == userId && s.IsActive)
                .ToListAsync(token);
            return (IReadOnlyList<PushSubscriptionRow>)result;
        }, cancellationToken);
    }

    public async Task UpdateSubscriptionActiveStatusAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken)
    {
        await _retryPolicy.ExecuteAsync("notifications.update_subscription", async token =>
        {
            var sub = await _dbContext.PushSubscriptions.FindAsync(new object[] { subscriptionId }, token);
            if (sub != null)
            {
                sub.IsActive = isActive;
                await _dbContext.SaveChangesAsync(token);
            }
        }, cancellationToken);
    }

    public async Task LogDeliveryAttemptAsync(NotificationDeliveryLogRow log, CancellationToken cancellationToken)
    {
        await _retryPolicy.ExecuteAsync("notifications.log_delivery", async token =>
        {
            _dbContext.NotificationDeliveryLog.Add(log);
            await _dbContext.SaveChangesAsync(token);
        }, cancellationToken);
    }

    public async Task RegisterSubscriptionAsync(Guid userId, string endpoint, string p256Dh, string auth, string? userAgent, CancellationToken cancellationToken)
    {
        await _retryPolicy.ExecuteAsync("notifications.register_subscription", async token =>
        {
            var existing = await _dbContext.PushSubscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == endpoint, token);

            if (existing != null)
            {
                existing.IsActive = true;
                existing.P256Dh = p256Dh;
                existing.Auth = auth;
                existing.UserAgent = userAgent;
                existing.LastSeenUtc = DateTimeOffset.UtcNow;
            }
            else
            {
                var newSub = new PushSubscriptionRow
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Endpoint = endpoint,
                    P256Dh = p256Dh,
                    Auth = auth,
                    UserAgent = userAgent,
                    IsActive = true,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    LastSeenUtc = DateTimeOffset.UtcNow
                };
                _dbContext.PushSubscriptions.Add(newSub);
            }
            await _dbContext.SaveChangesAsync(token);
        }, cancellationToken);
    }
}
