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
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(15);

    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;

    public EfNotificationRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
    }

    public async Task QueueNotificationAsync(NotificationQueueRow row, CancellationToken cancellationToken)
    {
        try
        {
            await _retryPolicy.ExecuteAsync("notifications.queue", async token =>
            {
                _dbContext.NotificationQueue.Add(row);
                await _dbContext.SaveChangesAsync(token);
            }, cancellationToken);
        }
        catch
        {
            var entry = _dbContext.Entry(row);
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<NotificationQueueRow>> ClaimPendingNotificationsAsync(int batchSize, CancellationToken cancellationToken)
    {
        return await _retryPolicy.ExecuteAsync("notifications.claim", async token =>
        {
            const string sql = """
                ;WITH candidates AS (
                    SELECT TOP (@BatchSize) *
                    FROM NotificationQueue WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE (Status = 'Pending' AND NextAttemptUtc <= SYSUTCDATETIME())
                       OR (Status = 'Processing' AND ProcessingStartedUtc <= @LeaseCutoffUtc)
                    ORDER BY NextAttemptUtc, CreatedUtc
                )
                UPDATE candidates
                SET Status = 'Processing',
                    ProcessingStartedUtc = SYSUTCDATETIME()
                OUTPUT inserted.Id, inserted.UserId, inserted.NotificationType, inserted.PayloadJson, inserted.Status, inserted.RetryCount, inserted.CreatedUtc, inserted.ProcessingStartedUtc, inserted.NextAttemptUtc, inserted.CompletedUtc, inserted.LastError;
                """;

            var connection = _dbContext.Database.GetDbConnection();
            var wasClosed = connection.State == ConnectionState.Closed;
            if (wasClosed)
            {
                await connection.OpenAsync(token);
            }

            try
            {
                var command = new CommandDefinition(
                    sql,
                    new
                    {
                        BatchSize = batchSize,
                        LeaseCutoffUtc = DateTimeOffset.UtcNow.Subtract(ProcessingLease)
                    },
                    cancellationToken: token);
                var result = await connection.QueryAsync<NotificationQueueRow>(command);
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

    public async Task<IReadOnlySet<Guid>> GetSuccessfulSubscriptionIdsAsync(
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        return await _retryPolicy.ExecuteAsync("notifications.get_successful_deliveries", async token =>
        {
            var subscriptionIds = await _dbContext.NotificationDeliveryLog
                .AsNoTracking()
                .Where(log => log.NotificationId == notificationId && log.Success)
                .Select(log => log.SubscriptionId)
                .Distinct()
                .ToListAsync(token);
            return (IReadOnlySet<Guid>)subscriptionIds.ToHashSet();
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

    public async Task RegisterSubscriptionAsync(
        Guid userId,
        string endpoint,
        string p256Dh,
        string auth,
        string? userAgent,
        string? replacedEndpoint,
        CancellationToken cancellationToken)
    {
        await _retryPolicy.ExecuteAsync("notifications.register_subscription", async token =>
        {
            if (!string.IsNullOrWhiteSpace(replacedEndpoint)
                && !string.Equals(replacedEndpoint, endpoint, StringComparison.Ordinal))
            {
                var replaced = await _dbContext.PushSubscriptions
                    .FirstOrDefaultAsync(
                        subscription => subscription.UserId == userId
                            && subscription.Endpoint == replacedEndpoint,
                        token);
                if (replaced is not null)
                {
                    replaced.IsActive = false;
                    replaced.LastSeenUtc = DateTimeOffset.UtcNow;
                }
            }

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

    public Task<IReadOnlyList<NotificationQueueRow>> GetHistoryAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("notifications.history", async token =>
        {
            var rows = await _dbContext.NotificationQueue.AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.CreatedUtc)
                .Skip(offset).Take(limit).ToListAsync(token);
            return (IReadOnlyList<NotificationQueueRow>)rows;
        }, cancellationToken);

    public Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("notifications.mark_read", async token =>
        {
            var row = await _dbContext.NotificationQueue.FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId, token);
            if (row is not null && row.ReadUtc is null) { row.ReadUtc = DateTimeOffset.UtcNow; await _dbContext.SaveChangesAsync(token); }
        }, cancellationToken);

    public Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("notifications.mark_all_read", async token =>
        {
            var rows = await _dbContext.NotificationQueue.Where(x => x.UserId == userId && x.ReadUtc == null).ToListAsync(token);
            var now = DateTimeOffset.UtcNow;
            foreach (var row in rows) row.ReadUtc = now;
            if (rows.Count > 0) await _dbContext.SaveChangesAsync(token);
        }, cancellationToken);

    public Task<bool> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("notifications.delete", async token =>
        {
            var row = await _dbContext.NotificationQueue
                .FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId, token);

            if (row is null)
            {
                return false;
            }

            var deliveryLogs = await _dbContext.NotificationDeliveryLog
                .Where(x => x.NotificationId == notificationId)
                .ToListAsync(token);
            if (deliveryLogs.Count > 0)
            {
                _dbContext.NotificationDeliveryLog.RemoveRange(deliveryLogs);
            }

            _dbContext.NotificationQueue.Remove(row);
            await _dbContext.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
}
