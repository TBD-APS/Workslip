using Microsoft.AspNetCore.Mvc;
using Workslip.Api.Helpers;
using Workslip.Api.Services;
using Workslip.Application.Auth;
using Workslip.Application.Documents;

namespace Workslip.Api.Endpoints;

public static class DocumentEndpoints
{
    private const long AttachmentRequestSizeLimit = DocumentAttachmentService.MaxAttachmentSizeBytes + (1024 * 1024);

    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var readGroup = app.MapReadGroup("/api/docs", "docs");

        readGroup.MapGet("/", async (HttpContext httpContext, int? limit, int? offset, string? search, IDocumentService service, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            return ResultExtensions.ToHttpResult(await service.ListAsync(limit, offset, search, cancellationToken));
        }).Produces<DocumentListResponse>();

        readGroup.MapGet("/{id:guid}", async (Guid id, HttpContext httpContext, IDocumentService service, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            return ResultExtensions.ToHttpResult(await service.GetByIdAsync(id, cancellationToken));
        }).Produces<DocumentDetailResponse>();

        readGroup.MapGet("/{id:guid}/attachments", async (
            Guid id,
            HttpContext httpContext,
            IDocumentAttachmentService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            return ResultExtensions.ToHttpResult(await service.ListAsync(id, cancellationToken));
        }).Produces<IReadOnlyList<DocumentAttachmentInfoResponse>>();

        readGroup.MapGet("/{id:guid}/attachments/{attachmentId:guid}", async (
            Guid id,
            Guid attachmentId,
            HttpContext httpContext,
            IDocumentAttachmentService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, attachmentId, cancellationToken);
            if (!result.IsSuccess)
                return ResultExtensions.ToHttpResult(result);

            HttpCacheHeaders.SetNoStore(httpContext);
            return Results.Stream(
                result.Value.Content,
                result.Value.ContentType,
                fileDownloadName: result.Value.FileName,
                enableRangeProcessing: false);
        });

        var adminGroup = app.MapAdminGroup("/api/docs", "docs");

        adminGroup.MapPost("/", async (
            CreateDocumentRequest request,
            HttpContext httpContext,
            ICurrentUserContext currentUser,
            IdempotentMutationService idempotency,
            IDocumentService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            if (!IdempotencyHttp.TryGetKey(httpContext, out var key))
                return Results.StatusCode(StatusCodes.Status428PreconditionRequired);

            var execution = await idempotency.ExecuteAsync(
                $"docs.create:{currentUser.OrganizationId}:{currentUser.UserId}",
                key,
                request,
                () => service.CreateAsync(request, cancellationToken),
                document => document,
                cancellationToken);

            if (execution.IsReplay)
                return Results.Content(execution.ReplayJson!, "application/json", System.Text.Encoding.UTF8, execution.ReplayStatusCode!.Value);

            if (execution.Conflict)
                return Results.Conflict(new { error = "idempotency_key_reused_with_different_request" });

            if (execution.InProgress)
                return Results.Conflict(new { error = "request_with_idempotency_key_in_progress" });

            return ResultExtensions.ToHttpResult(execution.Result!);
        }).Produces<DocumentDetailResponse>();

        adminGroup.MapPut("/{id:guid}", async (Guid id, UpdateDocumentRequest request, HttpContext httpContext, IDocumentService service, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            return ResultExtensions.ToHttpResult(await service.UpdateAsync(id, request, cancellationToken));
        }).Produces<DocumentDetailResponse>();

        adminGroup.MapDelete("/{id:guid}", async (Guid id, HttpContext httpContext, IDocumentService service, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.DeleteAsync(id, cancellationToken);
            return result.IsSuccess ? Results.NoContent() : ResultExtensions.ToHttpResult(result);
        });

        adminGroup.MapPost("/{id:guid}/attachments", async (
            Guid id,
            [FromForm] IFormFile file,
            HttpContext httpContext,
            IDocumentAttachmentService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            if (file is null)
                return Results.BadRequest(new { error = "No attachment uploaded." });

            await using var stream = file.OpenReadStream();
            var result = await service.UploadAsync(
                id,
                new DocumentAttachmentUpload(
                    stream,
                    file.Length,
                    file.FileName,
                    file.ContentType),
                cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        })
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(AttachmentRequestSizeLimit))
        .Produces<DocumentAttachmentInfoResponse>();

        adminGroup.MapDelete("/{id:guid}/attachments/{attachmentId:guid}", async (
            Guid id,
            Guid attachmentId,
            HttpContext httpContext,
            IDocumentAttachmentService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.DeleteAsync(id, attachmentId, cancellationToken);
            return result.IsSuccess ? Results.NoContent() : ResultExtensions.ToHttpResult(result);
        });

        return app;
    }
}
