using Microsoft.AspNetCore.Mvc;
using Workslip.Api.Helpers;
using Workslip.Application.Images;

namespace Workslip.Api.Endpoints;

public static class ImageEndpoints
{
    private const long MaxRequestSize = ImageService.MaxImageSizeBytes + (1024 * 1024);

    public static IEndpointRouteBuilder MapImageEndpoints(this IEndpointRouteBuilder app)
    {
        var jobReadGroup = app.MapGroup("/api/jobs")
            .WithTags("images")
            .RequireAuthorization(AuthPolicies.RequireReadAccess);

        jobReadGroup.MapGet("/{jobId:guid}/images", async (
            Guid jobId,
            IImageService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListJobImagesAsync(jobId, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).Produces<IReadOnlyList<ImageInfoResponse>>();

        jobReadGroup.MapGet("/{jobId:guid}/images/{imageId:guid}", async (
            Guid jobId,
            Guid imageId,
            HttpContext httpContext,
            IImageService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetJobImageAsync(jobId, imageId, cancellationToken);
            if (!result.IsSuccess)
            {
                return ResultExtensions.ToHttpResult(result);
            }

            HttpCacheHeaders.SetNoStore(httpContext);
            return Results.Stream(
                result.Value.Content,
                result.Value.ContentType,
                enableRangeProcessing: false);
        });

        var jobWriteGroup = app.MapGroup("/api/jobs")
            .WithTags("images")
            .RequireAuthorization(AuthPolicies.RequireUser);

        jobWriteGroup.MapPost("/{jobId:guid}/images", async (
            Guid jobId,
            [FromForm] IFormFile file,
            IImageService service,
            CancellationToken cancellationToken) =>
        {
            if (file is null)
            {
                return Results.BadRequest(new { error = "No image uploaded." });
            }

            await using var stream = file.OpenReadStream();
            var result = await service.UploadJobImageAsync(
                jobId,
                new ImageUpload(stream, file.Length, file.ContentType),
                cancellationToken);

            return ResultExtensions.ToHttpResult(result);
        })
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(MaxRequestSize))
        .Produces<ImageInfoResponse>();

        jobWriteGroup.MapDelete("/{jobId:guid}/images/{imageId:guid}", async (
            Guid jobId,
            Guid imageId,
            IImageService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteJobImageAsync(jobId, imageId, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        var profileReadGroup = app.MapGroup("/api/users")
            .WithTags("images")
            .RequireAuthorization(AuthPolicies.RequireUser);

        profileReadGroup.MapGet("/{userId:guid}/profile-image", async (
            Guid userId,
            HttpContext httpContext,
            IImageService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetProfileImageAsync(userId, cancellationToken);
            if (!result.IsSuccess)
            {
                return ResultExtensions.ToHttpResult(result);
            }

            HttpCacheHeaders.SetNoStore(httpContext);
            return Results.Stream(
                result.Value.Content,
                result.Value.ContentType,
                enableRangeProcessing: false);
        });

        var profileWriteGroup = app.MapGroup("/api/auth/me")
            .WithTags("images")
            .RequireAuthorization(AuthPolicies.RequireUser);

        profileWriteGroup.MapPut("/profile-image", async (
            [FromForm] IFormFile file,
            IImageService service,
            CancellationToken cancellationToken) =>
        {
            if (file is null)
            {
                return Results.BadRequest(new { error = "No image uploaded." });
            }

            await using var stream = file.OpenReadStream();
            var result = await service.UploadCurrentProfileImageAsync(
                new ImageUpload(stream, file.Length, file.ContentType),
                cancellationToken);

            return ResultExtensions.ToHttpResult(result);
        })
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(MaxRequestSize));

        profileWriteGroup.MapDelete("/profile-image", async (
            IImageService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteCurrentProfileImageAsync(cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        return app;
    }
}
