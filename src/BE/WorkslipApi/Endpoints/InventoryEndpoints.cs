using Microsoft.AspNetCore.Mvc;
using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Workslip.Api.Configuration;
using Workslip.Api.Helpers;
using Workslip.Application.Inventory;
using ZXing;
using ZXing.Common;

namespace Workslip.Api.Endpoints;

public sealed record InventoryQrLabelDocumentResponse(
    Guid MaterialId,
    string Name,
    string Sku,
    string Payload,
    string Svg);

public static class InventoryEndpoints
{
    private const long MaxScannerFrameBytes = 1_500_000;
    private const int MaxScannerFrameDimension = 2_048;

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

        user.MapPost("/scan-image", async (
            [FromForm] IFormFile file,
            IInventoryService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);

            if (file is null || file.Length <= 0 || file.Length > MaxScannerFrameBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["Kamerabilledet mangler eller er for stort."]
                });
            }

            if (!string.Equals(file.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(file.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["Kamerabilledet skal være JPEG eller PNG."]
                });
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var imageInfo = await Image.IdentifyAsync(stream, cancellationToken);
                if (imageInfo is null || imageInfo.Width <= 0 || imageInfo.Height <= 0 ||
                    imageInfo.Width > MaxScannerFrameDimension || imageInfo.Height > MaxScannerFrameDimension)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["file"] = ["Kamerabilledets dimensioner er ikke tilladt."]
                    });
                }

                stream.Position = 0;
                using var image = await Image.LoadAsync<Rgba32>(stream, cancellationToken);

                var reader = new ZXing.ImageSharp.BarcodeReader<Rgba32>
                {
                    AutoRotate = true,
                    Options = new DecodingOptions
                    {
                        TryHarder = true,
                        PossibleFormats = [BarcodeFormat.QR_CODE]
                    }
                };

                var decoded = reader.Decode(image);
                if (string.IsNullOrWhiteSpace(decoded?.Text))
                {
                    return Results.NotFound(new
                    {
                        error = "qr_not_detected",
                        message = "Ingen Workslip QR-kode blev fundet i billedet."
                    });
                }

                var result = await service.ScanAsync(new ScanInventoryRequest(decoded.Text), cancellationToken);
                return ResultExtensions.ToHttpResult(result);
            }
            catch (UnknownImageFormatException)
            {
                return InvalidImageResult();
            }
            catch (InvalidImageContentException)
            {
                return InvalidImageResult();
            }
        })
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(MaxScannerFrameBytes + 128_000))
        .Produces<InventoryScanResponse>();

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

    private static IResult InvalidImageResult() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["file"] = ["Kamerabilledet kunne ikke læses."]
    });

    private static string RenderQrSvg(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var code = new SvgQRCode(data);
        return code.GetGraphic(6);
    }
}