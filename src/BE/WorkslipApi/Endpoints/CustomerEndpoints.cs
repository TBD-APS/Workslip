using Microsoft.AspNetCore.Mvc;
using Workslip.Api.Helpers;
using Workslip.Api.Services;
using Workslip.Api.ViewModels;
using Workslip.Application.Auth;
using Workslip.Application.Customers;

namespace Workslip.Api.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var searchGroup = app.MapUserGroup("/api/customers", "customers");

        searchGroup.MapGet("/search", async (string? query, int? limit, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SearchAsync(query, limit, cancellationToken);
            return ResultExtensions.ToHttpResult(result, customers => customers.Select(CustomerViewModelBuilder.ToSearch).ToArray());
        }).Produces<List<CustomerSearchViewModel>>();

        searchGroup.MapGet("/favorite", async (int? limit, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetFavoriteAsync(limit ?? 3, cancellationToken);
            return ResultExtensions.ToHttpResult(result, customers => customers.Select(CustomerViewModelBuilder.ToSearch).ToArray());
        }).Produces<List<CustomerSearchViewModel>>();

        var userGroup = app.MapUserGroup("/api/customers", "customers");

        userGroup.MapGet("/", async ([AsParameters] ListQueryOptions query, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(
                query.Limit,
                query.Offset,
                query.Search,
                query.SortBy,
                query.SortDirection,
                cancellationToken);
            return ResultExtensions.ToHttpResult(result, CustomerViewModelBuilder.ToList);
        }).Produces<CustomerListViewModel>();

        userGroup.MapGet("/{id:guid}", async (Guid id, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetByIdAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result, CustomerViewModelBuilder.ToDetail);
        }).Produces<CustomerDetailViewModel>();

        var adminGroup = app.MapAdminGroup("/api/customers", "customers");

        adminGroup.MapPost("/", async (CreateCustomerRequest request, HttpContext httpContext, ICurrentUserContext currentUser, IdempotentMutationService idempotency, ICustomerService service, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            if (!IdempotencyHttp.TryGetKey(httpContext, out var key))
                return Results.StatusCode(StatusCodes.Status428PreconditionRequired);

            var execution = await idempotency.ExecuteAsync($"customers.create:{currentUser.OrganizationId}:{currentUser.UserId}", key, request, () => service.CreateAsync(request, cancellationToken), CustomerViewModelBuilder.ToDetail, cancellationToken);

            if (execution.IsReplay)
                return Results.Content(execution.ReplayJson!, "application/json", System.Text.Encoding.UTF8, execution.ReplayStatusCode!.Value);

            if (execution.Conflict)
                return Results.Conflict(new
                {
                    error = "idempotency_key_reused_with_different_request",
                    message = "Idempotensnøglen er allerede brugt til en anden anmodning."
                });

            if (execution.InProgress)
                return Results.Conflict(new
                {
                    error = "request_with_idempotency_key_in_progress",
                    message = "En anmodning med denne idempotensnøgle behandles allerede."
                });

            return ResultExtensions.ToHttpResult(execution.Result!, CustomerViewModelBuilder.ToDetail);
        }).Produces<CustomerDetailViewModel>();

        adminGroup.MapPut("/{id:guid}", async (Guid id, UpdateCustomerRequest request, HttpContext httpContext, ICustomerService service, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, CustomerViewModelBuilder.ToDetail);
        }).Produces<CustomerDetailViewModel>();

        adminGroup.MapPatch("/{id:guid}/favorite", async (Guid id, [FromBody] SetFavoriteRequest request, HttpContext httpContext, ICustomerService service, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.SetFavoriteAsync(id, request.IsFavorite, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        adminGroup.MapDelete("/{id:guid}", async (Guid id, HttpContext httpContext, ICustomerService service, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.DeleteAsync(id, cancellationToken);
            return result.IsSuccess
                ? Results.NoContent()
                : ResultExtensions.ToHttpResult(result);
        });

        adminGroup.MapPost("/import", async (
            IFormFile file,
            HttpContext httpContext,
            CustomerImportFileParser fileParser,
            ICustomerService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);

            CustomerImportParseResult parsed;
            try
            {
                parsed = fileParser.Parse(file);
            }
            catch (CustomerImportFormatException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var result = await service.ImportAsync(parsed.Customers, cancellationToken);
            return ResultExtensions.ToHttpResult(result, response => new CustomerImportViewModel(
                response.Imported,
                response.Duplicates,
                response.Skipped + parsed.Skipped,
                response.Failed,
                response.Errors.Select(error => new CustomerImportErrorViewModel(
                    error.RowNumber,
                    error.Field,
                    error.Message)).ToArray()));
        })
        .DisableAntiforgery()
        .RequireRateLimiting("customer-import")
        .WithMetadata(new RequestSizeLimitAttribute(CustomerImportFileParser.MaxUploadSize))
        .Produces<CustomerImportViewModel>();

        return app;
    }
}
