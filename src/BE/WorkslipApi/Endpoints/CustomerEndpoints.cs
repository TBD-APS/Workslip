using CsvHelper;
using Microsoft.AspNetCore.Mvc;
using Workslip.Api.Helpers;
using Workslip.Api.Services;
using Workslip.Api.ViewModels;
using Workslip.Application.Customers;
using Workslip.Application.Auth;

namespace Workslip.Api.Endpoints;

public static class CustomerEndpoints
{
    private const long MaxUploadSize = 10 * 1024 * 1024; // 10 MB

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
            return ResultExtensions.ToHttpResult(result, customer => CustomerViewModelBuilder.ToDetail(customer));
        }).Produces<CustomerDetailViewModel>();

        var adminGroup = app.MapAdminGroup("/api/customers", "customers");

        adminGroup.MapPost("/", async (CreateCustomerRequest request, HttpContext httpContext, ICurrentUserContext currentUser, IdempotentMutationService idempotency, ICustomerService service, CancellationToken cancellationToken) =>
        {
            if (!IdempotencyHttp.TryGetKey(httpContext, out var key))
                return Results.StatusCode(StatusCodes.Status428PreconditionRequired);
            var execution = await idempotency.ExecuteAsync($"customers.create:{currentUser.OrganizationId}:{currentUser.UserId}", key, request, () => service.CreateAsync(request, cancellationToken), CustomerViewModelBuilder.ToDetail, cancellationToken);
            
            if (execution.IsReplay) 
            return Results.Content(execution.ReplayJson!, "application/json", System.Text.Encoding.UTF8, execution.ReplayStatusCode!.Value);
            
            if (execution.Conflict)
             return Results.Conflict(new { error = "idempotency_key_reused_with_different_request" });
            
            if (execution.InProgress) return Results.Conflict(new { error = "request_with_idempotency_key_in_progress" });
                return ResultExtensions.ToHttpResult(execution.Result!, CustomerViewModelBuilder.ToDetail);
        
        }).Produces<CustomerDetailViewModel>();

        adminGroup.MapPut("/{id:guid}", async (Guid id, UpdateCustomerRequest request, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, customer => CustomerViewModelBuilder.ToDetail(customer));
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
                return Results.BadRequest(new { error = "No file uploaded." });
            }

            if (file.Length > MaxUploadSize)
            {
                return Results.BadRequest(new { error = $"File too large. Maximum size is {MaxUploadSize / 1024 / 1024} MB." });
            }

            if (!CustomerCsvParser.HasAllowedExtension(file.FileName) &&
                !CustomerCsvParser.IsAllowedContentType(file.ContentType))
            {
                return Results.BadRequest(new { error = "Only .csv files are accepted." });
            }

            IReadOnlyList<Application.Jobs.CustomerInfo> customers;
            int skipped;

            try
            {
                using var stream = file.OpenReadStream();
                var parseResult = CustomerCsvParser.Parse(stream, logger);
                customers = parseResult.Customers;
                skipped = parseResult.Skipped;
            }
            catch (CsvHelperException ex)
            {
                logger.LogError(ex, "Failed to parse CSV file {FileName}", file.FileName);
                return Results.BadRequest(new { error = $"Failed to parse CSV: {ex.Message}" });
            }

            var result = await service.ImportAsync(customers, cancellationToken);
            return ResultExtensions.ToHttpResult(result, response =>
                Results.Ok(new { imported = response.Imported, skipped }));
        }).DisableAntiforgery().RequireRateLimiting("customer-import").WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxUploadSize));

        return app;
    }
}
