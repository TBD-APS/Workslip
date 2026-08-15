using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Workslip.Application.Conversations;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class SqlJobConversationRepository(
    SqlDbContext dbContext,
    IDatabaseRetryPolicy retryPolicy) : IJobConversationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<ConversationMessageResponse>> ListAsync(
        Guid organizationId,
        Guid jobId,
        int limit,
        int offset,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "conversation.list",
            token => ListCoreAsync(organizationId, jobId, limit, offset, token),
            cancellationToken);

    public Task<int> GetUnreadCountAsync(
        Guid organizationId,
        Guid jobId,
        Guid userId,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "conversation.unread-count",
            token => GetUnreadCountCoreAsync(organizationId, jobId, userId, token),
            cancellationToken);

    public Task<ConversationMessageResponse> CreateAsync(
        Guid organizationId,
        Guid jobId,
        Guid authorUserId,
        string body,
        IReadOnlyList<Guid> mentionedUserIds,
        ConversationActionType? actionType,
        Guid? actionTargetUserId,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "conversation.create-message",
            token => CreateCoreAsync(
                organizationId,
                jobId,
                authorUserId,
                body,
                mentionedUserIds,
                actionType,
                actionTargetUserId,
                token),
            cancellationToken);

    public Task<ConversationMessageResponse?> GetByIdAsync(
        Guid organizationId,
        Guid jobId,
        Guid messageId,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "conversation.get-message",
            token => GetByIdCoreAsync(organizationId, jobId, messageId, token),
            cancellationToken);

    public Task<bool> TryResolveActionAsync(
        Guid organizationId,
        Guid jobId,
        Guid messageId,
        Guid resolverUserId,
        DateTimeOffset resolvedUtc,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "conversation.resolve-action",
            token => TryResolveActionCoreAsync(
                organizationId,
                jobId,
                messageId,
                resolverUserId,
                resolvedUtc,
                token),
            cancellationToken);

    public Task MarkReadAsync(
        Guid organizationId,
        Guid jobId,
        Guid userId,
        DateTimeOffset readUtc,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "conversation.mark-read",
            token => MarkReadCoreAsync(organizationId, jobId, userId, readUtc, token),
            cancellationToken);

    private async Task<IReadOnlyList<ConversationMessageResponse>> ListCoreAsync(
        Guid organizationId,
        Guid jobId,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var messages = TableName("JobConversationMessages");
        var users = TableName("Users");
        var pagination = IsSqlServer
            ? "OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY"
            : "LIMIT @Limit OFFSET @Offset";
        var sql = $"""
            SELECT
                m.Id,
                m.JobId,
                m.AuthorUserId,
                author.DisplayName AS AuthorDisplayName,
                m.Body,
                m.MentionedUserIdsJson,
                m.ActionType,
                m.ActionTargetUserId,
                target.DisplayName AS ActionTargetDisplayName,
                m.ActionStatus,
                m.ActionResolvedByUserId,
                resolver.DisplayName AS ActionResolvedByDisplayName,
                m.ActionResolvedUtc,
                m.CreatedUtc
            FROM {messages} m
            INNER JOIN {users} author
                ON author.OrganizationId = m.OrganizationId
                AND author.Id = m.AuthorUserId
            LEFT JOIN {users} target
                ON target.OrganizationId = m.OrganizationId
                AND target.Id = m.ActionTargetUserId
            LEFT JOIN {users} resolver
                ON resolver.OrganizationId = m.OrganizationId
                AND resolver.Id = m.ActionResolvedByUserId
            WHERE m.OrganizationId = @OrganizationId
              AND m.JobId = @JobId
            ORDER BY m.CreatedUtc DESC, m.Id DESC
            {pagination};
            """;

        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new { OrganizationId = organizationId, JobId = jobId, Limit = limit, Offset = offset },
                transaction,
                cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<ConversationMessageRow>(command);
            return rows.Reverse().Select(ToResponse).ToArray();
        }, cancellationToken);
    }

    private async Task<int> GetUnreadCountCoreAsync(
        Guid organizationId,
        Guid jobId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var messages = TableName("JobConversationMessages");
        var reads = TableName("JobConversationReads");
        var sql = $"""
            SELECT COUNT(1)
            FROM {messages} m
            LEFT JOIN {reads} r
                ON r.OrganizationId = m.OrganizationId
                AND r.JobId = m.JobId
                AND r.UserId = @UserId
            WHERE m.OrganizationId = @OrganizationId
              AND m.JobId = @JobId
              AND m.AuthorUserId <> @UserId
              AND (r.LastReadUtc IS NULL OR m.CreatedUtc > r.LastReadUtc);
            """;

        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new { OrganizationId = organizationId, JobId = jobId, UserId = userId },
                transaction,
                cancellationToken: cancellationToken);
            return await connection.QuerySingleAsync<int>(command);
        }, cancellationToken);
    }

    private async Task<ConversationMessageResponse> CreateCoreAsync(
        Guid organizationId,
        Guid jobId,
        Guid authorUserId,
        string body,
        IReadOnlyList<Guid> mentionedUserIds,
        ConversationActionType? actionType,
        Guid? actionTargetUserId,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var messages = TableName("JobConversationMessages");
        var sql = $"""
            INSERT INTO {messages}
                (Id, OrganizationId, JobId, AuthorUserId, Body, MentionedUserIdsJson,
                 ActionType, ActionTargetUserId, ActionStatus, ActionResolvedByUserId,
                 ActionResolvedUtc, CreatedUtc)
            VALUES
                (@Id, @OrganizationId, @JobId, @AuthorUserId, @Body, @MentionedUserIdsJson,
                 @ActionType, @ActionTargetUserId, @ActionStatus, NULL, NULL, @CreatedUtc);
            """;

        await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new
                {
                    Id = id,
                    OrganizationId = organizationId,
                    JobId = jobId,
                    AuthorUserId = authorUserId,
                    Body = body,
                    MentionedUserIdsJson = JsonSerializer.Serialize(mentionedUserIds, JsonOptions),
                    ActionType = actionType?.ToString(),
                    ActionTargetUserId = actionTargetUserId,
                    ActionStatus = actionType is null ? null : ConversationActionStatus.Pending.ToString(),
                    CreatedUtc = now
                },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
            return true;
        }, cancellationToken);

        return await GetByIdCoreAsync(organizationId, jobId, id, cancellationToken)
            ?? throw new InvalidOperationException("Created conversation message could not be read back.");
    }

    private async Task<ConversationMessageResponse?> GetByIdCoreAsync(
        Guid organizationId,
        Guid jobId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var messages = TableName("JobConversationMessages");
        var users = TableName("Users");
        var sql = $"""
            SELECT
                m.Id,
                m.JobId,
                m.AuthorUserId,
                author.DisplayName AS AuthorDisplayName,
                m.Body,
                m.MentionedUserIdsJson,
                m.ActionType,
                m.ActionTargetUserId,
                target.DisplayName AS ActionTargetDisplayName,
                m.ActionStatus,
                m.ActionResolvedByUserId,
                resolver.DisplayName AS ActionResolvedByDisplayName,
                m.ActionResolvedUtc,
                m.CreatedUtc
            FROM {messages} m
            INNER JOIN {users} author
                ON author.OrganizationId = m.OrganizationId
                AND author.Id = m.AuthorUserId
            LEFT JOIN {users} target
                ON target.OrganizationId = m.OrganizationId
                AND target.Id = m.ActionTargetUserId
            LEFT JOIN {users} resolver
                ON resolver.OrganizationId = m.OrganizationId
                AND resolver.Id = m.ActionResolvedByUserId
            WHERE m.OrganizationId = @OrganizationId
              AND m.JobId = @JobId
              AND m.Id = @MessageId;
            """;

        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new { OrganizationId = organizationId, JobId = jobId, MessageId = messageId },
                transaction,
                cancellationToken: cancellationToken);
            var row = await connection.QuerySingleOrDefaultAsync<ConversationMessageRow>(command);
            return row is null ? null : ToResponse(row);
        }, cancellationToken);
    }

    private async Task<bool> TryResolveActionCoreAsync(
        Guid organizationId,
        Guid jobId,
        Guid messageId,
        Guid resolverUserId,
        DateTimeOffset resolvedUtc,
        CancellationToken cancellationToken)
    {
        var messages = TableName("JobConversationMessages");
        var sql = $"""
            UPDATE {messages}
            SET ActionStatus = @CompletedStatus,
                ActionResolvedByUserId = @ResolverUserId,
                ActionResolvedUtc = @ResolvedUtc
            WHERE OrganizationId = @OrganizationId
              AND JobId = @JobId
              AND Id = @MessageId
              AND ActionTargetUserId = @ResolverUserId
              AND ActionStatus = @PendingStatus;
            """;

        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new
                {
                    OrganizationId = organizationId,
                    JobId = jobId,
                    MessageId = messageId,
                    ResolverUserId = resolverUserId,
                    ResolvedUtc = resolvedUtc,
                    PendingStatus = ConversationActionStatus.Pending.ToString(),
                    CompletedStatus = ConversationActionStatus.Completed.ToString()
                },
                transaction,
                cancellationToken: cancellationToken);
            return await connection.ExecuteAsync(command) == 1;
        }, cancellationToken);
    }

    private async Task MarkReadCoreAsync(
        Guid organizationId,
        Guid jobId,
        Guid userId,
        DateTimeOffset readUtc,
        CancellationToken cancellationToken)
    {
        var reads = TableName("JobConversationReads");
        var sql = IsSqlServer
            ? $"""
                MERGE {reads} AS target
                USING (SELECT @OrganizationId AS OrganizationId, @JobId AS JobId, @UserId AS UserId) AS source
                    ON target.OrganizationId = source.OrganizationId
                    AND target.JobId = source.JobId
                    AND target.UserId = source.UserId
                WHEN MATCHED THEN
                    UPDATE SET LastReadUtc = CASE WHEN target.LastReadUtc > @ReadUtc THEN target.LastReadUtc ELSE @ReadUtc END
                WHEN NOT MATCHED THEN
                    INSERT (OrganizationId, JobId, UserId, LastReadUtc)
                    VALUES (@OrganizationId, @JobId, @UserId, @ReadUtc);
                """
            : $"""
                INSERT INTO {reads} (OrganizationId, JobId, UserId, LastReadUtc)
                VALUES (@OrganizationId, @JobId, @UserId, @ReadUtc)
                ON CONFLICT(OrganizationId, JobId, UserId)
                DO UPDATE SET LastReadUtc = CASE
                    WHEN LastReadUtc > excluded.LastReadUtc THEN LastReadUtc
                    ELSE excluded.LastReadUtc
                END;
                """;

        await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new { OrganizationId = organizationId, JobId = jobId, UserId = userId, ReadUtc = readUtc },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
            return true;
        }, cancellationToken);
    }

    private ConversationMessageResponse ToResponse(ConversationMessageRow row)
    {
        ConversationActionResponse? action = null;
        if (Enum.TryParse<ConversationActionType>(row.ActionType, ignoreCase: true, out var actionType)
            && row.ActionTargetUserId is Guid targetUserId
            && Enum.TryParse<ConversationActionStatus>(row.ActionStatus, ignoreCase: true, out var actionStatus))
        {
            action = new ConversationActionResponse(
                actionType,
                targetUserId,
                row.ActionTargetDisplayName ?? "Medarbejder",
                actionStatus,
                row.ActionResolvedByUserId,
                row.ActionResolvedByDisplayName,
                row.ActionResolvedUtc);
        }

        return new ConversationMessageResponse(
            row.Id,
            row.JobId,
            row.AuthorUserId,
            row.AuthorDisplayName,
            row.Body,
            ParseMentionedUserIds(row.MentionedUserIdsJson),
            action,
            row.CreatedUtc);
    }

    private static IReadOnlyList<Guid> ParseMentionedUserIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Guid[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private bool IsSqlServer => string.Equals(
        dbContext.Database.ProviderName,
        "Microsoft.EntityFrameworkCore.SqlServer",
        StringComparison.Ordinal);

    private string TableName(string name) => IsSqlServer ? $"dbo.{name}" : name;

    private async Task<T> WithConnectionAsync<T>(
        Func<IDbConnection, IDbTransaction?, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            return await operation(connection, transaction);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private sealed class ConversationMessageRow
    {
        public Guid Id { get; init; }
        public Guid JobId { get; init; }
        public Guid AuthorUserId { get; init; }
        public string AuthorDisplayName { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public string MentionedUserIdsJson { get; init; } = "[]";
        public string? ActionType { get; init; }
        public Guid? ActionTargetUserId { get; init; }
        public string? ActionTargetDisplayName { get; init; }
        public string? ActionStatus { get; init; }
        public Guid? ActionResolvedByUserId { get; init; }
        public string? ActionResolvedByDisplayName { get; init; }
        public DateTimeOffset? ActionResolvedUtc { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }
}
