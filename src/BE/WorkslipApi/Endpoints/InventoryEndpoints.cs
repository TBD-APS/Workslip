using QRCoder;
using Workslip.Api.Configuration;
using Workslip.Api.Helpers;
using Workslip.Application.Inventory;

namespace Workslip.Api.Endpoints;

public sealed record InventoryQrLabelDocumentResponse(
    Guid MaterialId,
    string Name,
    string Sku,
    string Payload,
    string Svg);

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var user = app.MapUserGroup("/api/inventory", "inventory");
        var admin = app.MapAdminGroup("/api/inventory", "inventory");

        user.MapGet("/locations", async (
            IInventoryService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.ListLocationsAsync(cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).Produces<IReadOnlyList<InventoryLocationResponse>>();

        user.MapPost("/scan", async (
            ScanInventoryRequest request,
            IInventoryService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.ScanAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).Produces<InventoryScanResponse>();

        user.MapPost("/movements", async (
            ApplyInventoryMovementRequest request,
            IInventoryService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.ApplyMovementAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).Produces<InventoryMovementResponse>();

        admin.MapGet("/materials", async (
            IInventoryService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.ListMaterialsAsync(cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).Produces<IReadOnlyList<InventoryMaterialResponse>>();

        admin.MapPost("/materials", async (
            CreateInventoryMaterialRequest request,
            IInventoryService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.CreateMaterialAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).Produces<InventoryMaterialResponse>();

        admin.MapPost("/locations", async (
            CreateInventoryLocationRequest request,
            IInventoryService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.CreateLocationAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).Produces<InventoryLocationResponse>();

        admin.MapGet("/movements", async (
            int? limit,
            IInventoryService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.ListMovementsAsync(limit ?? 50, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).Produces<IReadOnlyList<InventoryMovementResponse>>();

        admin.MapGet("/materials/{materialId:guid}/qr-label", async (
            Guid materialId,
            IInventoryService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.GetQrLabelAsync(materialId, cancellationToken);
            return ResultExtensions.ToHttpResult(result, label => new InventoryQrLabelDocumentResponse(
                label.MaterialId,
                label.Name,
                label.Sku,
                label.Payload,
                RenderQrSvg(label.Payload)));
        }).Produces<InventoryQrLabelDocumentResponse>();

        return app;
    }

    private static string RenderQrSvg(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var code = new SvgQRCode(data);
        return code.GetGraphic(6);
    }
}
