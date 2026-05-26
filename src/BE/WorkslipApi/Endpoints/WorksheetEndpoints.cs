using Workslip.Application.Auth;

namespace Workslip.Api.Endpoints
{
    public static class WorksheetEndpoints
    {
        public static IEndpointRouteBuilder MapWorkSheetEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/worksheet").WithTags("worksheet");

            group.MapPost("/orginization/{orginizationId}/jobs/{jobId}/worksheets", x =>
            {
                return null;
            });

            return app;
        }
    }
}
