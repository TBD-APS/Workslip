using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Workslip.Application.Documents;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class SqlDocumentAttachmentRepository(
    SqlDbContext dbContext,
    IDatabaseRetryPolicy retryPolicy) : IDocumentAttachmentRepository
{
    public Task<IReadOnlyList<DocumentAttachmentInfoResponse>> ListAsync(
        Guid organizationId,
        Guid documentId,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "docs.attachments.list",
            token => ListCoreAsync(organizationId, documentId, token),
            cancellationToken);

    public Task<DocumentAttachmentInfoResponse?> GetAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "docs.attachments.get",
            token => GetCoreAsync(organizationId, documentId, attachmentId, token),
            cancellationToken);

    public async Task<DocumentAttachmentInfoResponse> CreateAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        string fileName,
        string contentType,
        long sizeBytes,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var attachments = TableName("KnowledgeDocumentAttachments");
        var now = DateTimeOffset.UtcNow;
        var sql = $"""
            INSERT INTO {attachments}
                (Id, OrganizationId, DocumentId, FileName, ContentType, SizeBytes, UploadedByUserId, CreatedAt)
            VALUES
                (@Id, @OrganizationId, @DocumentId, @FileName, @ContentType, @SizeBytes, @UploadedByUserId, @CreatedAt);
            """;

        await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new
                {
                    Id = attachmentId,
                    OrganizationId = organizationId,
                    DocumentId = documentId,
                    FileName = fileName,
                    ContentType = contentType,
                    SizeBytes = sizeBytes,
                    UploadedByUserId = actorUserId,
                    CreatedAt = now
                },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
            return true;
        }, cancellationToken);

        return await GetCoreAsync(organizationId, documentId, attachmentId, cancellationToken)
            ?? throw new InvalidOperationException("Created document attachment metadata could not be read back.");
    }

    public async Task<bool> DeleteAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var attachments = TableName("KnowledgeDocumentAttachments");
        var sql = $"""
            DELETE FROM {attachments}
            WHERE OrganizationId = @OrganizationId
              AND DocumentId = @DocumentId
              AND Id = @AttachmentId;
            """;

        var affected = await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new { OrganizationId = organizationId, DocumentId = documentId, AttachmentId = attachmentId },
                transaction,
                cancellationToken: cancellationToken);
            return await connection.ExecuteAsync(command);
        }, cancellationToken);

        return affected > 0;
    }

    private async Task<IReadOnlyList<DocumentAttachmentInfoResponse>> ListCoreAsync(
        Guid organizationId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var attachments = TableName("KnowledgeDocumentAttachments");
        var users = TableName("Users");
        var sql = $"""
            SELECT
                a.Id,
                a.DocumentId,
                a.FileName,
                a.ContentType,
                a.SizeBytes,
                a.CreatedAt,
                a.UploadedByUserId,
                uploadedBy.DisplayName AS UploadedByDisplayName
            FROM {attachments} a
            LEFT JOIN {users} uploadedBy
                ON uploadedBy.OrganizationId = a.OrganizationId
                AND uploadedBy.Id = a.UploadedByUserId
            WHERE a.OrganizationId = @OrganizationId
              AND a.DocumentId = @DocumentId
            ORDER BY a.CreatedAt ASC, a.FileName ASC, a.Id ASC;
            """;

        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new { OrganizationId = organizationId, DocumentId = documentId },
                transaction,
                cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<AttachmentRow>(command);
            return rows.Select(ToResponse).ToArray();
        }, cancellationToken);
    }

    private async Task<DocumentAttachmentInfoResponse?> GetCoreAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var attachments = TableName("KnowledgeDocumentAttachments");
        var users = TableName("Users");
        var sql = $"""
            SELECT
                a.Id,
                a.DocumentId,
                a.FileName,
                a.ContentType,
                a.SizeBytes,
                a.CreatedAt,
                a.UploadedByUserId,
                uploadedBy.DisplayName AS UploadedByDisplayName
            FROM {attachments} a
            LEFT JOIN {users} uploadedBy
                ON uploadedBy.OrganizationId = a.OrganizationId
                AND uploadedBy.Id = a.UploadedByUserId
            WHERE a.OrganizationId = @OrganizationId
              AND a.DocumentId = @DocumentId
              AND a.Id = @AttachmentId;
            """;

        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                sql,
                new { OrganizationId = organizationId, DocumentId = documentId, AttachmentId = attachmentId },
                transaction,
                cancellationToken: cancellationToken);
            var row = await connection.QuerySingleOrDefaultAsync<AttachmentRow>(command);
            return row is null ? null : ToResponse(row);
        }, cancellationToken);
    }

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

    private static DocumentAttachmentInfoResponse ToResponse(AttachmentRow row) => new(
        row.Id,
        row.DocumentId,
        row.FileName,
        row.ContentType,
        row.SizeBytes,
        row.CreatedAt,
        row.UploadedByUserId,
        row.UploadedByDisplayName);

    private sealed class AttachmentRow
    {
        public Guid Id { get; init; }
        public Guid DocumentId { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public Guid? UploadedByUserId { get; init; }
        public string? UploadedByDisplayName { get; init; }
    }
}
