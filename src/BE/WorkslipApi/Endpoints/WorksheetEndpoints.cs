using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Endpoints
{
    public static class WorksheetEndpoints
    {
        public static IEndpointRouteBuilder MapWorkSheetEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/worksheets").WithTags("worksheet").RequireAuthorization(AuthPolicies.RequireUser);

            group.MapPost("/jobs/{jobId:guid}", async (Guid jobId, UpsertWorksheetRequest request, IWorksheetService service, CancellationToken cancellationToken) =>
            {
                var upsertRequest = request with { JobId = jobId };
                var result = await service.UpsertAsync(upsertRequest, cancellationToken);
                return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
            }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);

            group.MapDelete("{worksheetId}/jobs/{jobId}", async (Guid worksheetId, Guid jobId, IWorksheetService service, CancellationToken cancellationToken) =>
            {
                var result = await service.DeleteAsync(worksheetId, jobId, cancellationToken);
                return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
            }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);

            group.MapGet("/my", async ([FromQuery] int? year, [FromQuery] int? month, IWorksheetService service, CancellationToken cancellationToken) =>
            {
                var result = await service.GetWorksheetsForUserAsync(year, month, cancellationToken);
                return ResultExtensions.ToHttpResult(result);
            }).Produces<MyWorksheetsMonthResponse>(StatusCodes.Status200OK);

            group.MapGet("/all", async ([FromQuery] int? year, [FromQuery] int? month, IWorksheetService service, CancellationToken cancellationToken) =>
            {
                var result = await service.GetAllWorksheetsAsync(year, month, cancellationToken);
                return ResultExtensions.ToHttpResult(result);
            }).Produces<MyWorksheetsMonthResponse>(StatusCodes.Status200OK).RequireAuthorization(AuthPolicies.RequireAdmin);

            group.MapGet("/all/report/power-bi", (IConfiguration configuration, HttpContext httpContext) =>
            {
                HttpCacheHeaders.SetNoStore(httpContext);
                var report = PowerBiReportUrlResolver.Resolve(configuration["PowerBiReport:Url"]);
                return Results.Ok(new
                {
                    url = report?.Url,
                    embedUrl = report?.EmbedUrl,
                });
            })
            .Produces(StatusCodes.Status200OK)
            .RequireAuthorization(AuthPolicies.RequireAdmin);

            group.MapGet("/all/report/power-bi/data", async (
                [FromQuery] int? historyMonths,
                HttpContext httpContext,
                [FromServices] ICurrentUserContext currentUser,
                [FromServices] IWorksheetRepository worksheets,
                [FromServices] SqlDbContext db,
                CancellationToken cancellationToken) =>
            {
                HttpCacheHeaders.SetNoStore(httpContext);

                if (currentUser.OrganizationId is not Guid organizationId)
                    return Results.Unauthorized();

                var selectedHistoryMonths = Math.Clamp(historyMonths ?? 24, 1, 120);
                var currentMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                var firstMonth = currentMonth.AddMonths(-(selectedHistoryMonths - 1));
                var worksheetEntries = new List<MyWorksheetEntryResponse>();

                for (var month = firstMonth; month <= currentMonth; month = month.AddMonths(1))
                {
                    var monthEnd = month.AddMonths(1).AddDays(-1);
                    worksheetEntries.AddRange(await worksheets.GetAllWorksheetsAsync(
                        organizationId,
                        month,
                        monthEnd,
                        cancellationToken));
                }

                var employees = await db.Users
                    .AsNoTracking()
                    .Where(user => user.OrganizationId == organizationId)
                    .OrderBy(user => user.DisplayName)
                    .Select(user => new
                    {
                        userId = user.Id,
                        employee = user.DisplayName,
                        role = user.Role,
                    })
                    .ToListAsync(cancellationToken);

                var workHours = worksheetEntries
                    .OrderBy(entry => entry.WorkDate)
                    .ThenBy(entry => entry.UserDisplayName)
                    .Select((entry, index) => new
                    {
                        worksheetId = $"export-{index + 1}",
                        jobId = entry.JobId,
                        userId = entry.UserId,
                        workDate = entry.WorkDate,
                        hours = entry.HoursWorked,
                        sleptOnJob = false,
                        billableHourlyRate = entry.BillableHourlyRate,
                        billableAmount = entry.BillableAmount,
                    })
                    .ToArray();

                var jobRows = await db.JobReports
                    .AsNoTracking()
                    .Where(job => job.OrganizationId == organizationId && !job.IsSoftDeleted)
                    .OrderBy(job => job.CreatedAt)
                    .Select(job => new
                    {
                        job.Id,
                        job.CustomerId,
                        job.ReportNumber,
                        job.Status,
                        job.JobType,
                        job.CreatedAt,
                        job.ReportDate,
                    })
                    .ToListAsync(cancellationToken);

                var jobs = jobRows.Select(job => new
                {
                    jobId = job.Id,
                    customerId = job.CustomerId,
                    reportNumber = job.ReportNumber,
                    status = job.Status,
                    jobType = job.JobType.ToString(),
                    createdDate = job.CreatedAt.Date,
                    reportDate = job.ReportDate?.Date,
                }).ToArray();

                var customers = await db.Customers
                    .AsNoTracking()
                    .Where(customer => customer.OrganizationId == organizationId)
                    .OrderBy(customer => customer.CreatedAt)
                    .Select(customer => new
                    {
                        customerId = customer.Id,
                        customerNumber = customer.CustomerNumber,
                        customer = customer.Name,
                        city = customer.City,
                        favorite = customer.IsFavorite,
                        createdDate = customer.CreatedAt.Date,
                    })
                    .ToListAsync(cancellationToken);

                return Results.Ok(new
                {
                    schemaVersion = 1,
                    generatedAtUtc = DateTimeOffset.UtcNow,
                    historyMonths = selectedHistoryMonths,
                    employees,
                    workHours,
                    jobs,
                    customers,
                });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization(AuthPolicies.RequireAdmin)
            .ExcludeFromDescription();

            group.MapGet("/all/report/pdf", async (
                [FromQuery] int? year,
                [FromQuery] int? month,
                HttpContext httpContext,
                IWorksheetService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetAllWorksheetsPdfAsync(year, month, cancellationToken);
                if (!result.IsSuccess)
                    return ResultExtensions.ToHttpResult(result);

                HttpCacheHeaders.SetNoStore(httpContext);
                return Results.File(result.Value.Content, "application/pdf", result.Value.FileName);
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthPolicies.RequireAdmin);

            group.MapGet("/all/report/pdf/preview", async (
                [FromQuery] int? year,
                [FromQuery] int? month,
                HttpContext httpContext,
                IWorksheetService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetAllWorksheetsPdfPreviewAsync(year, month, cancellationToken);
                if (!result.IsSuccess)
                    return ResultExtensions.ToHttpResult(result);

                HttpCacheHeaders.SetNoStore(httpContext);
                return Results.Ok(result.Value);
            })
            .Produces<MonthlyHoursPdfPreviewResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthPolicies.RequireAdmin);

            return app;
        }
    }
}
