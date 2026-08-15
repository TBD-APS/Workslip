using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Workslip.Application.Documents;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class SqlDocumentRepository(
    SqlDbContext dbContext,
    IDatabaseRetryPolicy retryPolicy) : IDocumentRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<DocumentListItemResponse>> ListAsync(
        Guid organizationId,
        int limit,
        int offset,
        string? search,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("docs.list", token => ListCoreAsync(organizationId, limit, offset, search, token), cancellationToken);

    public Task<int> CountAsync(Guid organizationId, string? search, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("docs.count", token => CountCoreAsync(organizationId, search, token), cancellationToken);

    public Task<DocumentDetailResponse?> GetByIdAsync(
        Guid organizationId,
        Guid id,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("docs.get-by-id", token => GetByIdCoreAsync(organizationId, id, token), cancellationToken);

    public Task<DocumentDetailResponse> CreateAsync(
        Guid organizationId,
        Guid? actorUserId,
        DocumentWriteData document,
        CancellationToken cancellationToken) =>
        CreateCoreAsync(organizationId, actorUserId, document, cancellationToken);

    public Task<DocumentDetailResponse?> UpdateAsync(
        Guid organizationId,
        Guid id,
        Guid? actorUserId,
        DocumentWriteData document,
        long expectedRevision,
        CancellationToken cancellationToken) =>
        UpdateCoreAsync(organizationId, id, actorUserId, document, expectedRevision, cancellationToken);

    public Task<bool> DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        DeleteCoreAsync(organizationId, id, cancellationToken);

    private async Task<IReadOnlyList<DocumentListItemResponse>> ListCoreAsync(
        Guid organizationId,
        int limit,
        int offset,
        string? search,
        CancellationToken cancellationToken)
    {
        var documents = TableName("KnowledgeDocuments");
        var users = TableName("Users");
        var searchPredicate = SearchPredicate("d");
        var pagination = IsSqlServer
            ? "OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY"
            : "LIMIT @Limit OFFSET @Offset";
        var preview = IsSqlServer ? "LEFT(d.Content, 240)" : "substr(d.Content, 1, 240)";

        var sql = $"""
            SELECT
                d.Id,
                d.Title,
                {preview} AS Preview,
                d.TagsJson,
                d.UpdatedAt,
                updatedBy.DisplayName AS UpdatedByDisplayName,
                d.Revision
            FROM {documents} d
            LEFT JOIN {users} updatedBy
                ON updatedBy.OrganizationId = d.OrganizationId
                AND updatedBy.Id = d.UpdatedByUserId
            WHERE d.OrganizationId = @OrganizationId
              AND {searchPredicate}
            ORDER BY d.UpdatedAt DESC, d.Title ASC, d.Id ASC
            {pagination};
            """;

        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new { OrganizationId = organizationId, Search = search, Limit = limit, Offset = offset },
                transaction,
                cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<DocumentListRow>(command);
            return rows.Select(ToListResponse).ToArray();
        }, cancellationToken);
    }

    private async Task<int> CountCoreAsync(Guid organizationId, string? search, CancellationToken cancellationToken)
    {
        var documents = TableName("KnowledgeDocuments");
        var sql = $"""
            SELECT COUNT(1)
            FROM {documents} d
            WHERE d.OrganizationId = @OrganizationId
              AND {SearchPredicate("d")};
            """;

        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new { OrganizationId = organizationId, Search = search },
                transaction,
                cancellationToken: cancellationToken);
            return await connection.QuerySingleAsync<int>(command);
        }, cancellationToken);
    }

    private async Task<DocumentDetailResponse?> GetByIdCoreAsync(
        Guid organizationId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var documents = TableName("KnowledgeDocuments");
        var users = TableName("Users");
        var sql = $"""
            SELECT
                d.Id,
                d.Title,
                d.Content,
                d.TagsJson,
                d.CreatedAt,
                d.UpdatedAt,
                d.CreatedByUserId,
                createdBy.DisplayName AS CreatedByDisplayName,
                d.UpdatedByUserId,
                updatedBy.DisplayName AS UpdatedByDisplayName,
                d.Revision
            FROM {documents} d
            LEFT JOIN {users} createdBy
                ON createdBy.OrganizationId = d.OrganizationId
                AND createdBy.Id = d.CreatedByUserId
            LEFT JOIN {users} updatedBy
                ON updatedBy.OrganizationId = d.OrganizationId
                AND updatedBy.Id = d.UpdatedByUserId
            WHERE d.OrganizationId = @OrganizationId
              AND d.Id = @Id;
            """;

        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new { OrganizationId = organizationId, Id = id },
                transaction,
                cancellationToken: cancellationToken);
            var row = await connection.QuerySingleOrDefaultAsync<DocumentDetailRow>(command);
            return row is null ? null : ToDetailResponse(row);
        }, cancellationToken);
    }

    private async Task<DocumentDetailResponse> CreateCoreAsync(
        Guid organizationId,
        Guid? actorUserId,
        DocumentWriteData document,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var documents = TableName("KnowledgeDocuments");
        var sql = $"""
            INSERT INTO {documents}
                (Id, OrganizationId, Title, Content, TagsJson, CreatedByUserId, UpdatedByUserId, CreatedAt, UpdatedAt, Revision)
            VALUES
                (@Id, @OrganizationId, @Title, @Content, @TagsJson, @ActorUserId, @ActorUserId, @Now, @Now, 1);
            """;

        await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new
                {
                    Id = id,
                    OrganizationId = organizationId,
                    document.Title,
                    document.Content,
                    TagsJson = JsonSerializer.Serialize(document.Tags, JsonOptions),
                    ActorUserId = actorUserId,
                    Now = now
                },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
            return true;
        }, cancellationToken);

        return await GetByIdCoreAsync(organizationId, id, cancellationToken)
            ?? throw new InvalidOperationException("Created internal document could not be read back.");
    }

    private async Task<DocumentDetailResponse?> UpdateCoreAsync(
        Guid organizationId,
        Guid id,
        Guid? actorUserId,
        DocumentWriteData document,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        var documents = TableName("KnowledgeDocuments");
        var now = DateTimeOffset.UtcNow;
        var sql = $"""
            UPDATE {documents}
            SET Title = @Title,
                Content = @Content,
                TagsJson = @TagsJson,
                UpdatedByUserId = @ActorUserId,
                UpdatedAt = @Now,
                Revision = Revision + 1
            WHERE OrganizationId = @OrganizationId
              AND Id = @Id
              AND Revision = @ExpectedRevision;
            """;

        var affected = await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new
                {
                    Id = id,
                    OrganizationId = organizationId,
                    document.Title,
                    document.Content,
                    TagsJson = JsonSerializer.Serialize(document.Tags, JsonOptions),
                    ActorUserId = actorUserId,
                    Now = now,
                    ExpectedRevision = expectedRevision
                },
                transaction,
                cancellationToken: cancellationToken);
            return await connection.ExecuteAsync(command);
        }, cancellationToken);

        if (affected == 0)
        {
            var current = await GetByIdCoreAsync(organizationId, id, cancellationToken);
            if (current is null)
                return null;
            throw new DocumentRevisionConflictException(id);
        }

        return await GetByIdCoreAsync(organizationId, id, cancellationToken);
    }

    private async Task<bool> DeleteCoreAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        var documents = TableName("KnowledgeDocuments");
        var sql = $"DELETE FROM {documents} WHERE OrganizationId = @OrganizationId AND Id = @Id;";

        var affected = await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new { OrganizationId = organizationId, Id = id },
                transaction,
                cancellationToken: cancellationToken);
            return await connection.ExecuteAsync(command);
        }, cancellationToken);

        return affected > 0;
    }

    private string SearchPredicate(string alias) =>
        IsSqlServer
            ? $"(@Search IS NULL OR CHARINDEX(@Search, {alias}.Title) > 0 OR CHARINDEX(@Search, {alias}.Content) > 0 OR CHARINDEX(@Search, {alias}.TagsJson) > 0)"
            : $"(@Search IS NULL OR instr({alias}.Title, @Search) > 0 OR instr({alias}.Content, @Search) > 0 OR instr({alias}.TagsJson, @Search) > 0)";

    private bool IsSqlServer =>
        string.Equals(
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

    private static DocumentListItemResponse ToListResponse(DocumentListRow row) =>
        new(
            row.Id,
            row.Title,
            row.Preview,
            ParseTags(row.TagsJson),
            row.UpdatedAt,
            row.UpdatedByDisplayName,
            row.Revision);

    private static DocumentDetailResponse ToDetailResponse(DocumentDetailRow row) =>
        new(
            row.Id,
            row.Title,
            row.Content,
            ParseTags(row.TagsJson),
            row.CreatedAt,
            row.UpdatedAt,
            row.CreatedByUserId,
            row.CreatedByDisplayName,
            row.UpdatedByUserId,
            row.UpdatedByDisplayName,
            row.Revision);

    private static IReadOnlyList<string> ParseTags(string? tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(tagsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed class DocumentListRow
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Preview { get; init; } = string.Empty;
        public string TagsJson { get; init; } = "[]";
        public DateTimeOffset UpdatedAt { get; init; }
        public string? UpdatedByDisplayName { get; init; }
        public long Revision { get; init; }
    }

    private sealed class DocumentDetailRow
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public string TagsJson { get; init; } = "[]";
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public Guid? CreatedByUserId { get; init; }
        public string? CreatedByDisplayName { get; init; }
        public Guid? UpdatedByUserId { get; init; }
        public string? UpdatedByDisplayName { get; init; }
        public long Revision { get; init; }
    }
}
