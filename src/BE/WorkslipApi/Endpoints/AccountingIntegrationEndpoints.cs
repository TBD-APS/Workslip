using Microsoft.AspNetCore.Mvc;
using Workslip.Api.Helpers;
using Workslip.Application.Integrations;

namespace Workslip.Api.Endpoints;

public static class AccountingIntegrationEndpoints
{
    public static IEndpointRouteBuilder MapAccountingIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/accounting", "accounting");

        group.MapGet("/status", async (
            [FromServices] IAccountingOperationsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            return Results.Ok(await service.GetStatusAsync(cancellationToken));
        }).Produces<AccountingConnectionStatusResponse>();

        group.MapPost("/customers/sync", async (
            [FromServices] IAccountingOperationsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            return Results.Ok(await service.SyncCustomersAsync(cancellationToken));
        }).Produces<AccountingCustomerSyncResponse>();

        group.MapPost("/jobs/{jobId:guid}/invoice-draft", async (
            Guid jobId,
            [FromServices] IAccountingOperationsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            try
            {
                return Results.Ok(await service.CreateDraftInvoiceAsync(jobId, cancellationToken));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new { error = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
        }).Produces<JobAccountingInvoiceResponse>();

        group.MapPost("/jobs/{jobId:guid}/invoice-refresh", async (
            Guid jobId,
            [FromServices] IAccountingOperationsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.RefreshInvoiceAsync(jobId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).Produces<JobAccountingInvoiceResponse>();

        group.MapGet("/jobs/{jobId:guid}/billable-items", async (
            Guid jobId,
            [FromServices] IAccountingOperationsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            return Results.Ok(await service.ListBillableItemsAsync(jobId, cancellationToken));
        }).Produces<IReadOnlyList<JobBillableItemResponse>>();

        group.MapPost("/jobs/{jobId:guid}/billable-items", async (
            Guid jobId,
            UpsertJobBillableItemRequest request,
            [FromServices] IAccountingOperationsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            try
            {
                var result = await service.UpsertBillableItemAsync(jobId, null, request, cancellationToken);
                return Results.Created($"/api/accounting/jobs/{jobId}/billable-items/{result.Id}", result);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new { error = exception.Message });
            }
        }).Produces<JobBillableItemResponse>(StatusCodes.Status201Created);

        group.MapPut("/jobs/{jobId:guid}/billable-items/{itemId:guid}", async (
            Guid jobId,
            Guid itemId,
            UpsertJobBillableItemRequest request,
            [FromServices] IAccountingOperationsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            try
            {
                return Results.Ok(await service.UpsertBillableItemAsync(jobId, itemId, request, cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new { error = exception.Message });
            }
        }).Produces<JobBillableItemResponse>();

        group.MapDelete("/jobs/{jobId:guid}/billable-items/{itemId:guid}", async (
            Guid jobId,
            Guid itemId,
            [FromServices] IAccountingOperationsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            await service.DeleteBillableItemAsync(jobId, itemId, cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/jobs/{jobId:guid}/documents", async (
            Guid jobId,
            [FromServices] IAccountingOperationsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            return Results.Ok(await service.ListLinkedDocumentsAsync(jobId, cancellationToken));
        }).Produces<IReadOnlyList<JobAccountingDocumentResponse>>();

        group.MapPost("/jobs/{jobId:guid}/documents/link", async (
            Guid jobId,
            LinkAccountingDocumentRequest request,
            [FromServices] IAccountingOperationsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            try
            {
                await service.LinkDocumentAsync(jobId, request, cancellationToken);
                return Results.NoContent();
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new { error = exception.Message });
            }
        });

        return app;
    }
}
