using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Workslip.Application.Documents;
using Workslip.Domain;

namespace Workslip.Infrastructure.Repositories;

public sealed class DapperDocumentRepository(ISqlConnectionFactory connectionFactory) : IDocumentRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DocumentTypeResponse> CreateDocumentTypeAsync(CreateDocumentTypeRequest request, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var documentTypeId = Guid.NewGuid();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into dbo.DocumentTypes (Id, Code, Name, Version, SchemaJson, IsActive, CreatedAt, UpdatedAt)
            values (@Id, @Code, @Name, @Version, @SchemaJson, 1, @CreatedAt, @UpdatedAt);
            """,
            new
            {
                Id = documentTypeId,
                request.Code,
                request.Name,
                request.Version,
                SchemaJson = request.Schema?.ToJsonString(JsonOptions),
                CreatedAt = now,
                UpdatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));

        foreach (var field in request.Fields)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into dbo.DocumentTypeFields (
                    Id, DocumentTypeId, FieldKey, Label, DataType, IsRequired, SortOrder, OptionsJson, CreatedAt, UpdatedAt
                )
                values (
                    @Id, @DocumentTypeId, @FieldKey, @Label, @DataType, @IsRequired, @SortOrder, @OptionsJson, @CreatedAt, @UpdatedAt
                );
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    DocumentTypeId = documentTypeId,
                    field.FieldKey,
                    field.Label,
                    field.DataType,
                    field.IsRequired,
                    field.SortOrder,
                    OptionsJson = field.Options?.ToJsonString(JsonOptions),
                    CreatedAt = now,
                    UpdatedAt = now
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        transaction.Commit();
        return (await GetDocumentTypeAsync(connection, documentTypeId, cancellationToken))!;
    }

    public async Task<IReadOnlyList<DocumentTypeResponse>> ListDocumentTypesAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<DocumentTypeRow>(new CommandDefinition(
            """
            select *
            from dbo.DocumentTypes
            where (@IncludeInactive = 1 or IsActive = 1)
            order by Code, Version desc;
            """,
            new { IncludeInactive = includeInactive },
            cancellationToken: cancellationToken));

        var result = new List<DocumentTypeResponse>();
        foreach (var row in rows)
        {
            result.Add(await ToDocumentTypeResponseAsync(connection, row, cancellationToken));
        }

        return result;
    }

    public async Task<DocumentTypeResponse?> GetDocumentTypeAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetDocumentTypeAsync(connection, id, cancellationToken);
    }

    public async Task<DocumentTypeFieldMutationResult> AddDocumentTypeFieldAsync(Guid documentTypeId, DocumentTypeFieldRequest request, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        if (!await DocumentTypeExistsAsync(connection, transaction, documentTypeId, cancellationToken))
        {
            return new(DocumentTypeFieldMutationStatus.DocumentTypeNotFound, null);
        }

        var existing = await GetDocumentTypeFieldAsync(connection, transaction, documentTypeId, request.FieldKey, cancellationToken);
        if (existing is not null)
        {
            return new(DocumentTypeFieldMutationStatus.FieldAlreadyExists, ToDocumentTypeFieldResponse(existing));
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into dbo.DocumentTypeFields (
                Id, DocumentTypeId, FieldKey, Label, DataType, IsRequired, SortOrder, OptionsJson, CreatedAt, UpdatedAt
            )
            values (
                @Id, @DocumentTypeId, @FieldKey, @Label, @DataType, @IsRequired, @SortOrder, @OptionsJson, @CreatedAt, @UpdatedAt
            );

            update dbo.DocumentTypes
            set UpdatedAt = @UpdatedAt
            where Id = @DocumentTypeId;
            """,
            new
            {
                Id = id,
                DocumentTypeId = documentTypeId,
                request.FieldKey,
                request.Label,
                request.DataType,
                request.IsRequired,
                request.SortOrder,
                OptionsJson = request.Options?.ToJsonString(JsonOptions),
                CreatedAt = now,
                UpdatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));

        var created = await GetDocumentTypeFieldAsync(connection, transaction, documentTypeId, request.FieldKey, cancellationToken);
        transaction.Commit();
        return new(DocumentTypeFieldMutationStatus.Success, created is null ? null : ToDocumentTypeFieldResponse(created));
    }

    public async Task<DocumentTypeFieldMutationResult> UpdateDocumentTypeFieldAsync(Guid documentTypeId, string fieldKey, UpdateDocumentTypeFieldRequest request, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        if (!await DocumentTypeExistsAsync(connection, transaction, documentTypeId, cancellationToken))
        {
            return new(DocumentTypeFieldMutationStatus.DocumentTypeNotFound, null);
        }

        var existing = await GetDocumentTypeFieldAsync(connection, transaction, documentTypeId, fieldKey, cancellationToken);
        if (existing is null)
        {
            return new(DocumentTypeFieldMutationStatus.FieldNotFound, null);
        }

        var now = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update dbo.DocumentTypeFields
            set Label = coalesce(@Label, Label),
                DataType = coalesce(@DataType, DataType),
                IsRequired = coalesce(@IsRequired, IsRequired),
                SortOrder = coalesce(@SortOrder, SortOrder),
                OptionsJson = coalesce(@OptionsJson, OptionsJson),
                UpdatedAt = @UpdatedAt
            where DocumentTypeId = @DocumentTypeId
              and FieldKey = @FieldKey;

            update dbo.DocumentTypes
            set UpdatedAt = @UpdatedAt
            where Id = @DocumentTypeId;
            """,
            new
            {
                DocumentTypeId = documentTypeId,
                FieldKey = fieldKey,
                request.Label,
                request.DataType,
                request.IsRequired,
                request.SortOrder,
                OptionsJson = request.Options?.ToJsonString(JsonOptions),
                UpdatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));

        var updated = await GetDocumentTypeFieldAsync(connection, transaction, documentTypeId, fieldKey, cancellationToken);
        transaction.Commit();
        return new(DocumentTypeFieldMutationStatus.Success, updated is null ? null : ToDocumentTypeFieldResponse(updated));
    }

    public async Task<DocumentTypeFieldMutationResult> DeleteDocumentTypeFieldAsync(Guid documentTypeId, string fieldKey, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        if (!await DocumentTypeExistsAsync(connection, transaction, documentTypeId, cancellationToken))
        {
            return new(DocumentTypeFieldMutationStatus.DocumentTypeNotFound, null);
        }

        var existing = await GetDocumentTypeFieldAsync(connection, transaction, documentTypeId, fieldKey, cancellationToken);
        if (existing is null)
        {
            return new(DocumentTypeFieldMutationStatus.FieldNotFound, null);
        }

        var now = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            delete from dbo.DocumentTypeFields
            where DocumentTypeId = @DocumentTypeId
              and FieldKey = @FieldKey;

            update dbo.DocumentTypes
            set UpdatedAt = @UpdatedAt
            where Id = @DocumentTypeId;
            """,
            new { DocumentTypeId = documentTypeId, FieldKey = fieldKey, UpdatedAt = now },
            transaction,
            cancellationToken: cancellationToken));

        transaction.Commit();
        return new(DocumentTypeFieldMutationStatus.Success, ToDocumentTypeFieldResponse(existing));
    }

    public async Task<ReportResponse> CreateReportAsync(CreateReportRequest request, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var reportId = Guid.NewGuid();
        var status = request.Status ?? (request.Files.Count > 0 ? ReportStatuses.Uploaded : ReportStatuses.Draft);
        var reviewStatus = request.ReviewStatus ?? ReviewStatuses.NotStarted;

        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into dbo.Reports (
                Id, OrganizationId, DocumentTypeId, CustomerId, SiteId, CaseId, ReportNumber, Title, Status, ReviewStatus,
                ReviewScore, OriginalFileId, GeneratedPdfFileId, PayloadJson, CreatedAt, UpdatedAt, SubmittedAt, ApprovedAt, ApprovedByUserId
            )
            values (
                @Id, @OrganizationId, @DocumentTypeId, @CustomerId, @SiteId, @CaseId, @ReportNumber, @Title, @Status, @ReviewStatus,
                @ReviewScore, null, null, @PayloadJson, @CreatedAt, @UpdatedAt, null, null, null
            );
            """,
            new
            {
                Id = reportId,
                request.OrganizationId,
                request.DocumentTypeId,
                request.CustomerId,
                request.SiteId,
                request.CaseId,
                request.ReportNumber,
                request.Title,
                Status = status,
                ReviewStatus = reviewStatus,
                request.ReviewScore,
                PayloadJson = request.Payload?.ToJsonString(JsonOptions),
                CreatedAt = now,
                UpdatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));

        Guid? originalFileId = null;
        Guid? generatedPdfFileId = null;
        foreach (var file in request.Files)
        {
            var created = await InsertFileAsync(connection, transaction, reportId, request.OrganizationId, file, now, cancellationToken);
            if (string.Equals(file.Purpose, DocumentFilePurposes.OriginalUpload, StringComparison.OrdinalIgnoreCase))
            {
                originalFileId ??= created.Id;
            }
            else if (string.Equals(file.Purpose, DocumentFilePurposes.GeneratedPdf, StringComparison.OrdinalIgnoreCase))
            {
                generatedPdfFileId ??= created.Id;
            }
        }

        foreach (var field in request.Fields)
        {
            await InsertFieldAsync(connection, transaction, reportId, field, now, cancellationToken);
        }

        if (originalFileId is not null || generatedPdfFileId is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                update dbo.Reports
                set OriginalFileId = @OriginalFileId,
                    GeneratedPdfFileId = @GeneratedPdfFileId,
                    UpdatedAt = @UpdatedAt
                where Id = @Id;
                """,
                new { Id = reportId, OriginalFileId = originalFileId, GeneratedPdfFileId = generatedPdfFileId, UpdatedAt = now },
                transaction,
                cancellationToken: cancellationToken));
        }

        await InsertAuditEventAsync(connection, transaction, request.OrganizationId, reportId, null, null, "created", "Report", null, ToJsonNode(new { reportId }), now, cancellationToken);
        transaction.Commit();

        return (await GetReportAsync(reportId, cancellationToken))!;
    }

    public async Task<IReadOnlyList<ReportListItemResponse>> ListReportsAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ReportRow>(new CommandDefinition(
            """
            select r.*,
                   dt.Code as DocumentTypeCode,
                   dt.Name as DocumentTypeName,
                   customer.CustomerName
            from dbo.Reports r
            join dbo.DocumentTypes dt on dt.Id = r.DocumentTypeId
            outer apply (
                select top 1 coalesce(nullif(rf.CorrectedValueText, ''), nullif(rf.NormalizedValueText, ''), nullif(rf.RawValueText, '')) as CustomerName
                from dbo.ReportFields rf
                where rf.ReportId = r.Id
                  and rf.FieldKey in ('customer.name', 'customerName', 'customer')
                order by rf.InstanceIndex
            ) customer
            where (@OrganizationId is null or r.OrganizationId = @OrganizationId)
              and (@DocumentTypeId is null or r.DocumentTypeId = @DocumentTypeId)
              and (@Status is null or r.Status = @Status)
              and (@ReviewStatus is null or r.ReviewStatus = @ReviewStatus)
            order by r.UpdatedAt desc
            offset @Offset rows fetch next @Limit rows only;
            """,
            new
            {
                query.OrganizationId,
                query.DocumentTypeId,
                query.Status,
                query.ReviewStatus,
                query.Limit,
                query.Offset
            },
            cancellationToken: cancellationToken));

        return rows.Select(ToListItem).ToArray();
    }

    public async Task<ReportResponse?> GetReportAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetReportAsync(connection, id, cancellationToken);
    }

    public async Task<ReportResponse?> UpdateReportAsync(Guid id, UpdateReportRequest request, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var existing = await connection.QuerySingleOrDefaultAsync<ReportRow>(new CommandDefinition(
            """
            select r.*, dt.Code as DocumentTypeCode, dt.Name as DocumentTypeName
            from dbo.Reports r
            join dbo.DocumentTypes dt on dt.Id = r.DocumentTypeId
            where r.Id = @Id;
            """,
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));

        if (existing is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update dbo.Reports
            set CustomerId = coalesce(@CustomerId, CustomerId),
                SiteId = coalesce(@SiteId, SiteId),
                CaseId = coalesce(@CaseId, CaseId),
                ReportNumber = coalesce(@ReportNumber, ReportNumber),
                Title = coalesce(@Title, Title),
                Status = coalesce(@Status, Status),
                ReviewStatus = coalesce(@ReviewStatus, ReviewStatus),
                ReviewScore = coalesce(@ReviewScore, ReviewScore),
                PayloadJson = coalesce(@PayloadJson, PayloadJson),
                SubmittedAt = case when @Status = 'Uploaded' then coalesce(SubmittedAt, @UpdatedAt) else SubmittedAt end,
                ApprovedAt = case when @Status = 'Approved' then @UpdatedAt else ApprovedAt end,
                UpdatedAt = @UpdatedAt
            where Id = @Id;
            """,
            new
            {
                Id = id,
                request.CustomerId,
                request.SiteId,
                request.CaseId,
                request.ReportNumber,
                request.Title,
                request.Status,
                request.ReviewStatus,
                request.ReviewScore,
                PayloadJson = request.Payload?.ToJsonString(JsonOptions),
                UpdatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));

        await InsertAuditEventAsync(connection, transaction, existing.OrganizationId, id, null, null, "updated", "Report", ToJsonNode(existing), ToJsonNode(request), now, cancellationToken);
        transaction.Commit();
        return await GetReportAsync(id, cancellationToken);
    }

    public async Task<DocumentFileResponse?> AddFileAsync(Guid reportId, DocumentFileRequest request, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var report = await connection.QuerySingleOrDefaultAsync<ReportRow>(new CommandDefinition(
            """
            select r.*, dt.Code as DocumentTypeCode, dt.Name as DocumentTypeName
            from dbo.Reports r
            join dbo.DocumentTypes dt on dt.Id = r.DocumentTypeId
            where r.Id = @ReportId;
            """,
            new { ReportId = reportId },
            transaction,
            cancellationToken: cancellationToken));

        if (report is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var created = await InsertFileAsync(connection, transaction, reportId, report.OrganizationId, request, now, cancellationToken);

        if (string.Equals(request.Purpose, DocumentFilePurposes.OriginalUpload, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(request.Purpose, DocumentFilePurposes.GeneratedPdf, StringComparison.OrdinalIgnoreCase))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                update dbo.Reports
                set OriginalFileId = case when @Purpose = 'OriginalUpload' then @FileId else OriginalFileId end,
                    GeneratedPdfFileId = case when @Purpose = 'GeneratedPdf' then @FileId else GeneratedPdfFileId end,
                    UpdatedAt = @UpdatedAt
                where Id = @ReportId;
                """,
                new { ReportId = reportId, FileId = created.Id, request.Purpose, UpdatedAt = now },
                transaction,
                cancellationToken: cancellationToken));
        }

        await InsertAuditEventAsync(connection, transaction, report.OrganizationId, reportId, created.Id, request.CreatedByUserId, "file_added", "DocumentFile", null, ToJsonNode(created), now, cancellationToken);
        transaction.Commit();
        return created;
    }

    public async Task<bool?> DeleteFileAsync(Guid reportId, Guid fileId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var report = await connection.QuerySingleOrDefaultAsync<ReportRow>(new CommandDefinition(
            """
            select r.*, dt.Code as DocumentTypeCode, dt.Name as DocumentTypeName
            from dbo.Reports r
            join dbo.DocumentTypes dt on dt.Id = r.DocumentTypeId
            where r.Id = @ReportId;
            """,
            new { ReportId = reportId },
            transaction,
            cancellationToken: cancellationToken));

        if (report is null)
        {
            return null;
        }

        var file = await connection.QuerySingleOrDefaultAsync<DocumentFileRow>(new CommandDefinition(
            "select * from dbo.DocumentFiles where Id = @FileId and ReportId = @ReportId;",
            new { ReportId = reportId, FileId = fileId },
            transaction,
            cancellationToken: cancellationToken));

        if (file is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update dbo.Reports
            set OriginalFileId = case when OriginalFileId = @FileId then null else OriginalFileId end,
                GeneratedPdfFileId = case when GeneratedPdfFileId = @FileId then null else GeneratedPdfFileId end,
                UpdatedAt = @UpdatedAt
            where Id = @ReportId;
            """,
            new { ReportId = reportId, FileId = fileId, UpdatedAt = now },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            update dbo.DocumentFiles
            set ReportId = null
            where Id = @FileId and ReportId = @ReportId;
            """,
            new { ReportId = reportId, FileId = fileId },
            transaction,
            cancellationToken: cancellationToken));

        await InsertAuditEventAsync(connection, transaction, report.OrganizationId, reportId, fileId, null, "file_deleted", "DocumentFile", ToJsonNode(file), null, now, cancellationToken);
        transaction.Commit();
        return true;
    }

    public async Task<ReportFieldResponse?> UpsertFieldAsync(Guid reportId, ReportFieldRequest request, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var report = await connection.QuerySingleOrDefaultAsync<ReportRow>(new CommandDefinition(
            """
            select r.*, dt.Code as DocumentTypeCode, dt.Name as DocumentTypeName
            from dbo.Reports r
            join dbo.DocumentTypes dt on dt.Id = r.DocumentTypeId
            where r.Id = @ReportId;
            """,
            new { ReportId = reportId },
            transaction,
            cancellationToken: cancellationToken));

        if (report is null)
        {
            return null;
        }

        var existing = await connection.QuerySingleOrDefaultAsync<ReportFieldRow>(new CommandDefinition(
            "select * from dbo.ReportFields where ReportId = @ReportId and FieldKey = @FieldKey and InstanceIndex = @InstanceIndex;",
            new { ReportId = reportId, request.FieldKey, request.InstanceIndex },
            transaction,
            cancellationToken: cancellationToken));

        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            await InsertFieldAsync(connection, transaction, reportId, request, now, cancellationToken);
        }
        else
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                update dbo.ReportFields
                set Label = @Label,
                    DataType = @DataType,
                    RawValueText = @RawValueText,
                    NormalizedValueText = @NormalizedValueText,
                    CorrectedValueText = @CorrectedValueText,
                    ValueJson = @ValueJson,
                    Confidence = @Confidence,
                    Status = @Status,
                    Source = @Source,
                    BoundingRegionsJson = @BoundingRegionsJson,
                    CorrectedByUserId = @CorrectedByUserId,
                    CorrectedAt = case when @CorrectedValueText is not null or @Status = 'Corrected' then @UpdatedAt else CorrectedAt end,
                    UpdatedAt = @UpdatedAt
                where Id = @Id;
                """,
                new
                {
                    existing.Id,
                    request.Label,
                    request.DataType,
                    RawValueText = request.RawValue,
                    NormalizedValueText = request.NormalizedValue,
                    CorrectedValueText = request.CorrectedValue,
                    ValueJson = request.Value?.ToJsonString(JsonOptions),
                    request.Confidence,
                    request.Status,
                    request.Source,
                    BoundingRegionsJson = request.BoundingRegions?.ToJsonString(JsonOptions),
                    request.CorrectedByUserId,
                    UpdatedAt = now
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "update dbo.Reports set UpdatedAt = @UpdatedAt where Id = @ReportId;",
            new { ReportId = reportId, UpdatedAt = now },
            transaction,
            cancellationToken: cancellationToken));

        await InsertAuditEventAsync(connection, transaction, report.OrganizationId, reportId, null, request.CorrectedByUserId, existing is null ? "field_created" : "field_updated", "ReportField", ToJsonNode(existing), ToJsonNode(request), now, cancellationToken);
        transaction.Commit();

        var row = await connection.QuerySingleAsync<ReportFieldRow>(new CommandDefinition(
            "select * from dbo.ReportFields where ReportId = @ReportId and FieldKey = @FieldKey and InstanceIndex = @InstanceIndex;",
            new { ReportId = reportId, request.FieldKey, request.InstanceIndex },
            cancellationToken: cancellationToken));

        return ToFieldResponse(row);
    }

    private static async Task<DocumentTypeResponse?> GetDocumentTypeAsync(IDbConnection connection, Guid id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<DocumentTypeRow>(new CommandDefinition(
            "select * from dbo.DocumentTypes where Id = @Id;",
            new { Id = id },
            cancellationToken: cancellationToken));

        return row is null ? null : await ToDocumentTypeResponseAsync(connection, row, cancellationToken);
    }

    private static async Task<bool> DocumentTypeExistsAsync(IDbConnection connection, IDbTransaction transaction, Guid id, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(1) from dbo.DocumentTypes where Id = @Id;",
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken)) > 0;

    private static async Task<DocumentTypeFieldRow?> GetDocumentTypeFieldAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid documentTypeId,
        string fieldKey,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<DocumentTypeFieldRow>(new CommandDefinition(
            """
            select *
            from dbo.DocumentTypeFields
            where DocumentTypeId = @DocumentTypeId
              and FieldKey = @FieldKey;
            """,
            new { DocumentTypeId = documentTypeId, FieldKey = fieldKey },
            transaction,
            cancellationToken: cancellationToken));

    private static async Task<DocumentTypeResponse> ToDocumentTypeResponseAsync(IDbConnection connection, DocumentTypeRow row, CancellationToken cancellationToken)
    {
        var fields = await connection.QueryAsync<DocumentTypeFieldRow>(new CommandDefinition(
            "select * from dbo.DocumentTypeFields where DocumentTypeId = @DocumentTypeId order by SortOrder, FieldKey;",
            new { DocumentTypeId = row.Id },
            cancellationToken: cancellationToken));

        return new DocumentTypeResponse(
            row.Id,
            row.Code,
            row.Name,
            row.Version,
            ToJsonObject(row.SchemaJson),
            row.IsActive,
            fields.Select(ToDocumentTypeFieldResponse).ToArray(),
            row.CreatedAt,
            row.UpdatedAt);
    }

    private static async Task<ReportResponse?> GetReportAsync(IDbConnection connection, Guid id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ReportRow>(new CommandDefinition(
            """
            select r.*, dt.Code as DocumentTypeCode, dt.Name as DocumentTypeName
            from dbo.Reports r
            join dbo.DocumentTypes dt on dt.Id = r.DocumentTypeId
            where r.Id = @Id;
            """,
            new { Id = id },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        var files = await connection.QueryAsync<DocumentFileRow>(new CommandDefinition(
            "select * from dbo.DocumentFiles where ReportId = @Id order by CreatedAt;",
            new { Id = id },
            cancellationToken: cancellationToken));

        var fields = await connection.QueryAsync<ReportFieldRow>(new CommandDefinition(
            "select * from dbo.ReportFields where ReportId = @Id order by FieldKey, InstanceIndex;",
            new { Id = id },
            cancellationToken: cancellationToken));

        return new ReportResponse(
            row.Id,
            row.OrganizationId,
            row.DocumentTypeId,
            row.DocumentTypeCode,
            row.DocumentTypeName,
            row.CustomerId,
            row.SiteId,
            row.CaseId,
            row.ReportNumber,
            row.Title,
            row.Status,
            row.ReviewStatus,
            row.ReviewScore,
            row.OriginalFileId,
            row.GeneratedPdfFileId,
            ToJsonObject(row.PayloadJson),
            files.Select(ToFileResponse).ToArray(),
            fields.Select(ToFieldResponse).ToArray(),
            row.CreatedAt,
            row.UpdatedAt,
            row.SubmittedAt,
            row.ApprovedAt,
            row.ApprovedByUserId);
    }

    private static async Task<DocumentFileResponse> InsertFileAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid reportId,
        Guid organizationId,
        DocumentFileRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into dbo.DocumentFiles (
                Id, OrganizationId, ReportId, Purpose, StorageAccountName, ContainerName, BlobName, BlobVersionId,
                FileName, ContentType, FileSizeBytes, Sha256Hash, CreatedAt, CreatedByUserId
            )
            values (
                @Id, @OrganizationId, @ReportId, @Purpose, @StorageAccountName, @ContainerName, @BlobName, @BlobVersionId,
                @FileName, @ContentType, @FileSizeBytes, @Sha256Hash, @CreatedAt, @CreatedByUserId
            );
            """,
            new
            {
                Id = id,
                OrganizationId = organizationId,
                ReportId = reportId,
                request.Purpose,
                request.StorageAccountName,
                request.ContainerName,
                request.BlobName,
                request.BlobVersionId,
                request.FileName,
                request.ContentType,
                request.FileSizeBytes,
                request.Sha256Hash,
                CreatedAt = now,
                request.CreatedByUserId
            },
            transaction,
            cancellationToken: cancellationToken));

        return new DocumentFileResponse(
            id,
            organizationId,
            reportId,
            request.Purpose,
            request.StorageAccountName,
            request.ContainerName,
            request.BlobName,
            request.BlobVersionId,
            request.FileName,
            request.ContentType,
            request.FileSizeBytes,
            request.Sha256Hash,
            now,
            request.CreatedByUserId);
    }

    private static async Task InsertFieldAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid reportId,
        ReportFieldRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into dbo.ReportFields (
                Id, ReportId, FieldKey, InstanceIndex, Label, DataType, RawValueText, NormalizedValueText,
                CorrectedValueText, ValueJson, Confidence, Status, Source, BoundingRegionsJson,
                CorrectedByUserId, CorrectedAt, CreatedAt, UpdatedAt
            )
            values (
                @Id, @ReportId, @FieldKey, @InstanceIndex, @Label, @DataType, @RawValueText, @NormalizedValueText,
                @CorrectedValueText, @ValueJson, @Confidence, @Status, @Source, @BoundingRegionsJson,
                @CorrectedByUserId, @CorrectedAt, @CreatedAt, @UpdatedAt
            );
            """,
            new
            {
                Id = Guid.NewGuid(),
                ReportId = reportId,
                request.FieldKey,
                request.InstanceIndex,
                request.Label,
                request.DataType,
                RawValueText = request.RawValue,
                NormalizedValueText = request.NormalizedValue,
                CorrectedValueText = request.CorrectedValue,
                ValueJson = request.Value?.ToJsonString(JsonOptions),
                request.Confidence,
                request.Status,
                request.Source,
                BoundingRegionsJson = request.BoundingRegions?.ToJsonString(JsonOptions),
                request.CorrectedByUserId,
                CorrectedAt = request.CorrectedValue is not null || string.Equals(request.Status, FieldStatuses.Corrected, StringComparison.OrdinalIgnoreCase) ? now : (DateTimeOffset?)null,
                CreatedAt = now,
                UpdatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task InsertAuditEventAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid organizationId,
        Guid? reportId,
        Guid? fileId,
        Guid? actorId,
        string eventType,
        string entityType,
        JsonObject? before,
        JsonObject? after,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            insert into dbo.AuditEvents (
                Id, OrganizationId, ReportId, FileId, ActorId, EventType, EntityType, BeforeJson, AfterJson, CorrelationId, CreatedAt
            )
            values (
                @Id, @OrganizationId, @ReportId, @FileId, @ActorId, @EventType, @EntityType, @BeforeJson, @AfterJson, null, @CreatedAt
            );
            """,
            new
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ReportId = reportId,
                FileId = fileId,
                ActorId = actorId,
                EventType = eventType,
                EntityType = entityType,
                BeforeJson = before?.ToJsonString(JsonOptions),
                AfterJson = after?.ToJsonString(JsonOptions),
                CreatedAt = now
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    private static ReportListItemResponse ToListItem(ReportRow row) =>
        new(
            row.Id,
            row.OrganizationId,
            row.DocumentTypeId,
            row.DocumentTypeCode,
            row.DocumentTypeName,
            row.CustomerId,
            row.CustomerName,
            row.SiteId,
            row.CaseId,
            row.ReportNumber,
            row.Title,
            row.Status,
            row.ReviewStatus,
            row.ReviewScore,
            row.OriginalFileId,
            row.GeneratedPdfFileId,
            row.CreatedAt,
            row.UpdatedAt,
            row.SubmittedAt,
            row.ApprovedAt,
            row.ApprovedByUserId);

    private static DocumentTypeFieldResponse ToDocumentTypeFieldResponse(DocumentTypeFieldRow row) =>
        new(
            row.Id,
            row.FieldKey,
            row.Label,
            row.DataType,
            row.IsRequired,
            row.SortOrder,
            ToJsonObject(row.OptionsJson),
            row.CreatedAt,
            row.UpdatedAt);

    private static DocumentFileResponse ToFileResponse(DocumentFileRow row) =>
        new(
            row.Id,
            row.OrganizationId,
            row.ReportId,
            row.Purpose,
            row.StorageAccountName,
            row.ContainerName,
            row.BlobName,
            row.BlobVersionId,
            row.FileName,
            row.ContentType,
            row.FileSizeBytes,
            row.Sha256Hash,
            row.CreatedAt,
            row.CreatedByUserId);

    private static ReportFieldResponse ToFieldResponse(ReportFieldRow row) =>
        new(
            row.Id,
            row.ReportId,
            row.FieldKey,
            row.InstanceIndex,
            row.Label,
            row.DataType,
            row.RawValueText,
            row.NormalizedValueText,
            row.CorrectedValueText,
            ToJsonObject(row.ValueJson),
            row.Confidence,
            row.Status,
            row.Source,
            ToJsonObject(row.BoundingRegionsJson),
            row.CorrectedByUserId,
            row.CorrectedAt,
            row.CreatedAt,
            row.UpdatedAt);

    private static JsonObject? ToJsonObject(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject;

    private static JsonObject ToJsonNode<T>(T value) =>
        JsonSerializer.SerializeToNode(value, JsonOptions)?.AsObject() ?? [];

    private sealed class DocumentTypeRow
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public int Version { get; init; }
        public string? SchemaJson { get; init; }
        public bool IsActive { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class DocumentTypeFieldRow
    {
        public Guid Id { get; init; }
        public string FieldKey { get; init; } = "";
        public string Label { get; init; } = "";
        public string DataType { get; init; } = "";
        public bool IsRequired { get; init; }
        public int SortOrder { get; init; }
        public string? OptionsJson { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class ReportRow
    {
        public Guid Id { get; init; }
        public Guid OrganizationId { get; init; }
        public Guid DocumentTypeId { get; init; }
        public string DocumentTypeCode { get; init; } = "";
        public string DocumentTypeName { get; init; } = "";
        public Guid? CustomerId { get; init; }
        public string? CustomerName { get; init; }
        public Guid? SiteId { get; init; }
        public Guid? CaseId { get; init; }
        public string? ReportNumber { get; init; }
        public string? Title { get; init; }
        public string Status { get; init; } = "";
        public string ReviewStatus { get; init; } = "";
        public decimal? ReviewScore { get; init; }
        public Guid? OriginalFileId { get; init; }
        public Guid? GeneratedPdfFileId { get; init; }
        public string? PayloadJson { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public DateTimeOffset? SubmittedAt { get; init; }
        public DateTimeOffset? ApprovedAt { get; init; }
        public Guid? ApprovedByUserId { get; init; }
    }

    private sealed class DocumentFileRow
    {
        public Guid Id { get; init; }
        public Guid OrganizationId { get; init; }
        public Guid? ReportId { get; init; }
        public string Purpose { get; init; } = "";
        public string StorageAccountName { get; init; } = "";
        public string ContainerName { get; init; } = "";
        public string BlobName { get; init; } = "";
        public string? BlobVersionId { get; init; }
        public string FileName { get; init; } = "";
        public string ContentType { get; init; } = "";
        public long FileSizeBytes { get; init; }
        public string? Sha256Hash { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public Guid? CreatedByUserId { get; init; }
    }

    private sealed class ReportFieldRow
    {
        public Guid Id { get; init; }
        public Guid ReportId { get; init; }
        public string FieldKey { get; init; } = "";
        public int InstanceIndex { get; init; }
        public string Label { get; init; } = "";
        public string DataType { get; init; } = "";
        public string? RawValueText { get; init; }
        public string? NormalizedValueText { get; init; }
        public string? CorrectedValueText { get; init; }
        public string? ValueJson { get; init; }
        public decimal? Confidence { get; init; }
        public string Status { get; init; } = "";
        public string Source { get; init; } = "";
        public string? BoundingRegionsJson { get; init; }
        public Guid? CorrectedByUserId { get; init; }
        public DateTimeOffset? CorrectedAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }
}
