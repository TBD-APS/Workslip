/*using Workslip.Application.Documents;
using Workslip.Domain;

namespace Workslip.Api.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var documentTypes = app.MapGroup("/api/document-types").WithTags("Document types");

        documentTypes.MapPost("/", async (
            CreateDocumentTypeRequest request,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {

            var created = await repository.CreateDocumentTypeAsync(request, cancellationToken);
            return Results.Created($"/api/document-types/{created.Id}", created);
        });

        documentTypes.MapGet("/", async (
            bool? includeInactive,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {
            var result = await repository.ListDocumentTypesAsync(includeInactive ?? false, cancellationToken);
            return Results.Ok(result);
        });

        documentTypes.MapPost("/{id:guid}/fields", async (
            Guid id,
            DocumentTypeFieldRequest request,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {
            var result = await repository.AddDocumentTypeFieldAsync(id, request, cancellationToken);
            return result.Status switch
            {
                DocumentTypeFieldMutationStatus.Success => Results.Created($"/api/document-types/{id}/fields/{request.FieldKey}", result.Field),
                DocumentTypeFieldMutationStatus.DocumentTypeNotFound => Results.NotFound(new { message = $"Document type {id} was not found." }),
                DocumentTypeFieldMutationStatus.FieldAlreadyExists => Results.Conflict(new { message = $"Field '{request.FieldKey}' already exists on document type {id}.", field = result.Field }),
                _ => Results.Problem("Unable to add document type field.")
            };
        });

        documentTypes.MapPatch("/{id:guid}/fields/{fieldKey}", async (
            Guid id,
            string fieldKey,
            UpdateDocumentTypeFieldRequest request,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {

            var result = await repository.UpdateDocumentTypeFieldAsync(id, fieldKey, request, cancellationToken);
            return FieldMutationResult(id, fieldKey, result);
        });

        documentTypes.MapDelete("/{id:guid}/fields/{fieldKey}", async (
            Guid id,
            string fieldKey,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {
            var result = await repository.DeleteDocumentTypeFieldAsync(id, fieldKey, cancellationToken);
            return result.Status switch
            {
                DocumentTypeFieldMutationStatus.Success => Results.NoContent(),
                _ => FieldMutationResult(id, fieldKey, result)
            };
        });

        documentTypes.MapGet("/{id:guid}/view-model", async (
            Guid id,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {
            var documentType = await repository.GetDocumentTypeAsync(id, cancellationToken);
            return documentType;
        });

        var reports = app.MapGroup("/api/reports").WithTags("Reports");

        reports.MapPost("/", async (
            CreateReportRequest request,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {
            var created = await repository.CreateReportAsync(request, cancellationToken);
            return Results.Created($"/api/reports/{created.Id}", created);
        });

        reports.MapGet("/", async (
            Guid? organizationId,
            Guid? documentTypeId,
            string? status,
            string? reviewStatus,
            int? limit,
            int? offset,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(status) && !ReportStatuses.All.Contains(status))
            {
                return Validation([new("status", $"status must be one of: {string.Join(", ", ReportStatuses.All)}.")]);
            }

            if (!string.IsNullOrWhiteSpace(reviewStatus) && !ReviewStatuses.All.Contains(reviewStatus))
            {
                return Validation([new("reviewStatus", $"reviewStatus must be one of: {string.Join(", ", ReviewStatuses.All)}.")]);
            }

            var query = new ReportQuery(
                organizationId,
                documentTypeId,
                status,
                reviewStatus,
                Math.Clamp(limit ?? 50, 1, 200),
                Math.Max(offset ?? 0, 0));

            return Results.Ok(await repository.ListReportsAsync(query, cancellationToken));
        });

        reports.MapGet("/{id:guid}", async (
            Guid id,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {
            var report = await repository.GetReportAsync(id, cancellationToken);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        reports.MapGet("/{id:guid}/view-model", async (
            Guid id,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {
            var report = await repository.GetReportAsync(id, cancellationToken);
            if (report is null)
            {
                return Results.NotFound();
            }

            var documentType = await repository.GetDocumentTypeAsync(report.DocumentTypeId, cancellationToken);
            return documentType;
        });

        reports.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateReportRequest request,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {

            var updated = await repository.UpdateReportAsync(id, request, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        reports.MapPost("/{id:guid}/files", async (
            Guid id,
            DocumentFileRequest request,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {
            var created = await repository.AddFileAsync(id, request, cancellationToken);
            return created is null ? Results.NotFound() : Results.Created($"/api/reports/{id}/files/{created.Id}", created);
        });

        reports.MapPost("/{id:guid}/files/upload", async (
            Guid id,
            HttpRequest request,
            IDocumentRepository repository,
            IDocumentFileStorage fileStorage,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return Validation([new("files", "Upload must be sent as multipart/form-data.")]);
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var files = form.Files.GetFiles("files");
            if (files.Count == 0)
            {
                files = form.Files.ToArray();
            }

            var errors = ValidateUploadForm(form, files);
            if (errors.Count > 0)
            {
                return Validation(errors);
            }

            var report = await repository.GetReportAsync(id, cancellationToken);
            if (report is null)
            {
                return Results.NotFound(new { message = $"Report {id} was not found." });
            }

            var purpose = FirstFormValue(form, "purpose") ?? DocumentFilePurposes.Attachment;
            var createdByUserId = ParseOptionalGuid(FirstFormValue(form, "createdByUserId"));
            var created = new List<DocumentFileResponse>();

            foreach (var file in files)
            {
                await using var stream = file.OpenReadStream();
                var stored = await fileStorage.SaveAsync(id, stream, file.FileName, file.ContentType, cancellationToken);
                var added = await repository.AddFileAsync(id, new DocumentFileRequest(
                    purpose,
                    stored.StorageAccountName,
                    stored.ContainerName,
                    stored.BlobName,
                    stored.BlobVersionId,
                    stored.FileName,
                    stored.ContentType,
                    stored.FileSizeBytes,
                    stored.Sha256Hash,
                    createdByUserId), cancellationToken);

                if (added is not null)
                {
                    created.Add(added);
                }
            }

            return Results.Created($"/api/reports/{id}/files", created);
        });

        reports.MapGet("/{id:guid}/files/{fileId:guid}/content", async (
            Guid id,
            Guid fileId,
            IDocumentRepository repository,
            IDocumentFileStorage fileStorage,
            CancellationToken cancellationToken) =>
        {
            var report = await repository.GetReportAsync(id, cancellationToken);
            if (report is null)
            {
                return Results.NotFound(new { message = $"Report {id} was not found." });
            }

            var file = report.Files.FirstOrDefault(candidate => candidate.Id == fileId);
            if (file is null)
            {
                return Results.NotFound(new { message = $"File {fileId} was not found on report {id}." });
            }

            var content = await fileStorage.OpenReadAsync(file, cancellationToken);
            return content is null
                ? Results.NotFound(new { message = $"File content for {fileId} was not found in local storage." })
                : Results.File(content.Content, content.ContentType, content.FileName, enableRangeProcessing: true);
        });

        reports.MapDelete("/{id:guid}/files/{fileId:guid}", async (
            Guid id,
            Guid fileId,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {
            var deleted = await repository.DeleteFileAsync(id, fileId, cancellationToken);
            return deleted switch
            {
                null => Results.NotFound(new { message = $"Report {id} was not found." }),
                false => Results.NotFound(new { message = $"File {fileId} was not found on report {id}." }),
                true => Results.NoContent()
            };
        });

        reports.MapPut("/{id:guid}/fields", async (
            Guid id,
            ReportFieldRequest request,
            IDocumentRepository repository,
            CancellationToken cancellationToken) =>
        {
            var upserted = await repository.UpsertFieldAsync(id, request, cancellationToken);
            return upserted is null ? Results.NotFound() : Results.Ok(upserted);
        });

        return app;
    }

    private static IResult Validation(IReadOnlyList<DocumentValidationError> errors) =>
        Results.ValidationProblem(errors
            .GroupBy(error => error.Field)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray()));

    private static IReadOnlyList<DocumentValidationError> ValidateUploadForm(IFormCollection form, IReadOnlyList<IFormFile> files)
    {
        var errors = new List<DocumentValidationError>();

        if (files.Count == 0)
        {
            errors.Add(new("files", "At least one file is required."));
        }

        foreach (var file in files)
        {
            if (file.Length <= 0)
            {
                errors.Add(new("files", $"File '{file.FileName}' is empty."));
            }
        }

        var purpose = FirstFormValue(form, "purpose") ?? DocumentFilePurposes.Attachment;
        if (!DocumentFilePurposes.All.Contains(purpose))
        {
            errors.Add(new("purpose", $"purpose must be one of: {string.Join(", ", DocumentFilePurposes.All)}."));
        }

        var createdByUserId = FirstFormValue(form, "createdByUserId");
        if (!string.IsNullOrWhiteSpace(createdByUserId) && !Guid.TryParse(createdByUserId, out _))
        {
            errors.Add(new("createdByUserId", "createdByUserId must be a valid GUID."));
        }

        return errors;
    }

    private static string? FirstFormValue(IFormCollection form, string key)
    {
        if (!form.TryGetValue(key, out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static Guid? ParseOptionalGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;

    private static IResult FieldMutationResult(Guid documentTypeId, string fieldKey, DocumentTypeFieldMutationResult result) =>
        result.Status switch
        {
            DocumentTypeFieldMutationStatus.Success => Results.Ok(result.Field),
            DocumentTypeFieldMutationStatus.DocumentTypeNotFound => Results.NotFound(new { message = $"Document type {documentTypeId} was not found." }),
            DocumentTypeFieldMutationStatus.FieldNotFound => Results.NotFound(new { message = $"Field '{fieldKey}' was not found on document type {documentTypeId}." }),
            DocumentTypeFieldMutationStatus.FieldAlreadyExists => Results.Conflict(new { message = $"Field '{fieldKey}' already exists on document type {documentTypeId}.", field = result.Field }),
            _ => Results.Problem("Unable to mutate document type field.")
        };
}
*/