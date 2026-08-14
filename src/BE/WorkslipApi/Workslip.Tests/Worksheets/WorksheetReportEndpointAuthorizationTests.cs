using Ardalis.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Api.Endpoints;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Xunit;

namespace Workslip.Tests.Worksheets;

public sealed class WorksheetReportEndpointAuthorizationTests
{
    [Fact]
    public async Task PowerBiReportLinkEndpoint_RequiresAdminPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IWorksheetService, StubWorksheetService>();
        await using var app = builder.Build();
        app.MapWorkSheetEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(route => route.RoutePattern.RawText == "/api/worksheets/all/report/power-bi");

        var policies = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(metadata => metadata.Policy)
            .Where(policy => policy is not null)
            .ToList();

        Assert.Contains(AuthPolicies.RequireAdmin, policies);
    }

    private sealed class StubWorksheetService : IWorksheetService
    {
        public Task<Result<JobReportSummaryResponse>> UpsertAsync(
            UpsertWorksheetRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<JobReportSummaryResponse>.NotFound());

        public Task<Result<JobReportSummaryResponse>> DeleteAsync(
            Guid worksheetId,
            Guid jobId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<JobReportSummaryResponse>.NotFound());

        public Task<Result<MyWorksheetsMonthResponse>> GetWorksheetsForUserAsync(
            int? year,
            int? month,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<MyWorksheetsMonthResponse>.NotFound());

        public Task<Result<MyWorksheetsMonthResponse>> GetAllWorksheetsAsync(
            int? year,
            int? month,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<MyWorksheetsMonthResponse>.NotFound());

        public Task<Result<MonthlyHoursPdfResponse>> GetAllWorksheetsPdfAsync(
            int? year,
            int? month,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<MonthlyHoursPdfResponse>.NotFound());

        public Task<Result<MonthlyHoursPdfPreviewResponse>> GetAllWorksheetsPdfPreviewAsync(
            int? year,
            int? month,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<MonthlyHoursPdfPreviewResponse>.NotFound());
    }
}
