using CsvHelper;
using Microsoft.AspNetCore.Mvc;
using Workslip.Api.Helpers;
using Workslip.Api.Services;
using Workslip.Api.ViewModels;
using Workslip.Application.Auth;
using Workslip.Application.Customers;

namespace Workslip.Api.Endpoints;

public static class CustomerEndpoints
{
    private const long MaxUploadSize = 10 * 1024 * 1024;

    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var searchGroup = app.MapReadGroup("/api/customers", "customers");

        searchGroup.MapGet("/search", async (string? query, int? limit, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SearchAsync(query, limit, cancellationToken);
            return ResultExtensions.ToHttpResult(result, customers => customers.Select(CustomerViewModelBuilder.ToSearch).ToArray());
        }).Produces<List<CustomerSearchViewModel>>();

        searchGroup.MapGet("/suggest", async (string? query, int? limit, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SearchAsync(query, limit, cancellationToken);
            return ResultExtensions.ToHttpResult(result, customers => customers.Select(CustomerViewModelBuilder.ToSearch).ToArray());
        }).Produces<List<CustomerSearchViewModel>>();

        searchGroup.MapGet("/top", async (int? limit, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetTopAsync(limit ?? 3, cancellationToken);
            return ResultExtensions.ToHttpResult(result, customers => customers.Select(CustomerViewModelBuilder.ToSearch).ToArray());
        }).Produces<List<CustomerSearchViewModel>>();

        var userGroup = app.MapUserGroup("/api/customers", "customers");

        userGroup.MapGet("/", async (int? limit, int? offset, string? search, string? sortBy, string? sortDirection, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(limit, offset, search, sortBy, sortDirection, cancellationToken);
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
            if (!IdempotencyHttp.TryGetKey(httpContext, out var key))
            {
                return Results.StatusCode(StatusCodes.Status428PreconditionRequired);
            }

            var execution = await idempotency.ExecuteAsync(
                $"customers.create:{currentUser.OrganizationId}:{currentUser.UserId}",
                key,
                request,
                () => service.CreateAsync(request, cancellationToken),
                CustomerViewModelBuilder.ToDetail,
                cancellationToken);

            if (execution.IsReplay)
            {
                return Results.Content(execution.ReplayJson!, "application/json", System.Text.Encoding.UTF8, execution.ReplayStatusCode!.Value);
            }

            if (execution.Conflict)
            {
                return Results.Conflict(new { error = "idempotency_key_reused_with_different_request" });
            }

            if (execution.InProgress)
            {
                return Results.Conflict(new { error = "request_with_idempotency_key_in_progress" });
            }

            return ResultExtensions.ToHttpResult(execution.Result!, CustomerViewModelBuilder.ToDetail);
        }).Produces<CustomerDetailViewModel>();

        adminGroup.MapPut("/{id:guid}", async (Guid id, UpdateCustomerRequest request, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, CustomerViewModelBuilder.ToDetail);
        }).Produces<CustomerDetailViewModel>();

        adminGroup.MapPatch("/{id:guid}/top", async (Guid id, [FromBody] SetTopRequest request, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SetTopAsync(id, request.IsTop, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        adminGroup.MapDelete("/{id:guid}", async (Guid id, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        adminGroup.MapPost("/import", async (
            IFormFile file,
            ICustomerService service,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("CustomerImport");
            if (file is null or { Length: 0 })
            {
                return Results.BadRequest(new { error = "Der blev ikke uploadet en fil." });
            }

            if (file.Length > MaxUploadSize)
            {
                return Results.BadRequest(new { error = $"Filen er for stor. Maksimum er {MaxUploadSize / 1024 / 1024} MB." });
            }

            var isCsv = CustomerCsvParser.HasAllowedExtension(file.FileName) || CustomerCsvParser.IsAllowedContentType(file.ContentType);
            var isExcel = CustomerExcelParser.HasAllowedExtension(file.FileName) || CustomerExcelParser.IsAllowedContentType(file.ContentType);
            if (!isCsv && !isExcel)
            {
                return Results.BadRequest(new { error = "Kun .xlsx- og .csv-filer accepteres." });
            }

            CustomerImportParseResult parsed;
            try
            {
                using var stream = file.OpenReadStream();
                parsed = CustomerExcelParser.HasAllowedExtension(file.FileName) || CustomerExcelParser.IsAllowedContentType(file.ContentType)
                    ? CustomerExcelParser.Parse(stream)
                    : CustomerCsvParser.Parse(stream, logger);
            }
            catch (CustomerImportFormatException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (CsvHelperException ex)
            {
                logger.LogWarning(ex, "Failed to parse customer CSV {FileName}", file.FileName);
                return Results.BadRequest(new { error = "CSV-filen kunne ikke læses." });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to parse customer import file {FileName}", file.FileName);
                return Results.BadRequest(new { error = "Filen kunne ikke læses som en gyldig kundeimport." });
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
        .WithMetadata(new RequestSizeLimitAttribute(MaxUploadSize))
        .Produces<CustomerImportViewModel>();

        return app;
    }
}
