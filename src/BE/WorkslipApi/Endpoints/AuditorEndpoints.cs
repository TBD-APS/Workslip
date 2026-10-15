using Workslip.Api.Helpers;
using Workslip.Application.Auditing;
using Workslip.Application.Images;

namespace Workslip.Api.Endpoints;

public static class AuditorEndpoints
{
    public static IEndpointRouteBuilder MapAuditorEndpoints(this IEndpointRouteBuilder app)
    {
        var auditor = app.MapGroup("/api/auditor")
            .WithTags("auditor")
            .RequireAuthorization(AuthPolicies.RequireAuditor);

        auditor.MapGet("/organizations", async (
            IAuditorService service,
            CancellationToken cancellationToken) =>
            ResultExtensions.ToHttpResult(await service.ListOrganizationsAsync(cancellationToken)))
            .Produces<IReadOnlyList<AuditorOrganizationSummaryResponse>>();

        auditor.MapGet("/organizations/{organizationId:guid}/reports", async (
            Guid organizationId,
            string? search,
            string? installationType,
            int? limit,
            int? offset,
            IAuditorService service,
            CancellationToken cancellationToken) =>
            ResultExtensions.ToHttpResult(await service.ListReportsAsync(
                organizationId,
                search,
                installationType,
                limit,
                offset,
                cancellationToken)))
            .Produces<AuditorReportListResponse>();

        auditor.MapGet("/organizations/{organizationId:guid}/reports/{jobId:guid}", async (
            Guid organizationId,
            Guid jobId,
            IAuditorService service,
            CancellationToken cancellationToken) =>
            ResultExtensions.ToHttpResult(await service.GetReportAsync(organizationId, jobId, cancellationToken)))
            .Produces<AuditorReportDetailResponse>();

        auditor.MapGet("/organizations/{organizationId:guid}/reports/{jobId:guid}/images", async (
            Guid organizationId,
            Guid jobId,
            IAuditorService service,
            CancellationToken cancellationToken) =>
            ResultExtensions.ToHttpResult(await service.ListImagesAsync(organizationId, jobId, cancellationToken)))
            .Produces<IReadOnlyList<ImageInfoResponse>>();

        auditor.MapGet("/organizations/{organizationId:guid}/reports/{jobId:guid}/images/{imageId:guid}", async (
            Guid organizationId,
            Guid jobId,
            Guid imageId,
            HttpContext httpContext,
            IAuditorService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetImageAsync(organizationId, jobId, imageId, cancellationToken);
            if (!result.IsSuccess)
                return ResultExtensions.ToHttpResult(result);

            HttpCacheHeaders.SetNoStore(httpContext);
            return Results.Stream(result.Value.Content, result.Value.ContentType, enableRangeProcessing: false);
        });

        auditor.MapPost("/organizations/{organizationId:guid}/reports/{jobId:guid}/findings", async (
            Guid organizationId,
            Guid jobId,
            CreateAuditorFindingRequest request,
            IAuditorService service,
            CancellationToken cancellationToken) =>
            ResultExtensions.ToHttpResult(await service.CreateFindingAsync(organizationId, jobId, request, cancellationToken)))
            .Produces<AuditorFindingResponse>();

        auditor.MapPatch("/organizations/{organizationId:guid}/reports/{jobId:guid}/findings/{findingId:guid}", async (
            Guid organizationId,
            Guid jobId,
            Guid findingId,
            UpdateAuditorFindingRequest request,
            IAuditorService service,
            CancellationToken cancellationToken) =>
            ResultExtensions.ToHttpResult(await service.UpdateFindingAsync(organizationId, jobId, findingId, request, cancellationToken)))
            .Produces<AuditorFindingResponse>();

        var management = app.MapGroup("/api/auditor/admin")
            .WithTags("auditor-admin")
            .RequireAuthorization(AuthPolicies.RequireSuperAdmin);

        management.MapGet("/assignments", async (
            IAuditorService service,
            CancellationToken cancellationToken) =>
            ResultExtensions.ToHttpResult(await service.ListAssignmentsAsync(cancellationToken)))
            .Produces<IReadOnlyList<AuditorAssignmentAdminResponse>>();

        management.MapPost("/assignments", async (
            CreateAuditorAssignmentRequest request,
            IAuditorService service,
            CancellationToken cancellationToken) =>
            ResultExtensions.ToHttpResult(await service.CreateAssignmentAsync(request, cancellationToken)))
            .Produces<AuditorAssignmentAdminResponse>();

        management.MapPatch("/assignments/{assignmentId:guid}", async (
            Guid assignmentId,
            UpdateAuditorAssignmentRequest request,
            IAuditorService service,
            CancellationToken cancellationToken) =>
            ResultExtensions.ToHttpResult(await service.UpdateAssignmentAsync(assignmentId, request, cancellationToken)))
            .Produces<AuditorAssignmentAdminResponse>();

        var company = app.MapGroup("/api/auditor/findings")
            .WithTags("auditor-follow-up")
            .RequireAuthorization(AuthPolicies.RequireUser);

        company.MapPost("/{findingId:guid}/evidence", async (
            Guid findingId,
            AddAuditorFindingEvidenceRequest request,
            IAuditorService service,
            CancellationToken cancellationToken) =>
            ResultExtensions.ToHttpResult(await service.AddCompanyEvidenceAsync(findingId, request, cancellationToken)))
            .Produces<AuditorFindingResponse>();

        return app;
    }
}
