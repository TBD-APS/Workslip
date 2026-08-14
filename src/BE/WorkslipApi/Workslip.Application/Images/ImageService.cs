using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Users;

namespace Workslip.Application.Images;

public sealed class ImageService(
    IImageStorage storage,
    IJobRepository jobs,
    IUserRepository users,
    ICurrentUserContext currentUser) : IImageService
{
    public const long MaxImageSizeBytes = 10 * 1024 * 1024;

    public async Task<Result<IReadOnlyList<ImageInfoResponse>>> ListJobImagesAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var access = await GetAccessibleJobAsync(jobId, cancellationToken);
        if (!access.IsSuccess)
        {
            return MapFailure<IReadOnlyList<ImageInfoResponse>>(access.Status);
        }

        var images = await storage.ListJobImagesAsync(
            access.Value.OrganizationId,
            jobId,
            cancellationToken);

        return Result<IReadOnlyList<ImageInfoResponse>>.Success(images);
    }

    public async Task<Result<ImageInfoResponse>> UploadJobImageAsync(
        Guid jobId,
        ImageUpload upload,
        CancellationToken cancellationToken)
    {
        var access = await GetAccessibleJobAsync(jobId, cancellationToken);
        if (!access.IsSuccess)
        {
            return MapFailure<ImageInfoResponse>(access.Status);
        }

        var validated = await ValidateAndBufferAsync(upload, cancellationToken);
        if (!validated.IsSuccess)
        {
            return Result<ImageInfoResponse>.Invalid(validated.ValidationErrors);
        }

        await using var content = validated.Value.Content;
        var image = await storage.UploadJobImageAsync(
            access.Value.OrganizationId,
            jobId,
            Guid.NewGuid(),
            content,
            validated.Value.ContentType,
            cancellationToken);

        return Result<ImageInfoResponse>.Success(image);
    }

    public async Task<Result<ImageFileResponse>> GetJobImageAsync(
        Guid jobId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        var access = await GetAccessibleJobAsync(jobId, cancellationToken);
        if (!access.IsSuccess)
        {
            return MapFailure<ImageFileResponse>(access.Status);
        }

        var image = await storage.GetJobImageAsync(
            access.Value.OrganizationId,
            jobId,
            imageId,
            cancellationToken);

        return image is null
            ? Result<ImageFileResponse>.NotFound()
            : Result<ImageFileResponse>.Success(image);
    }

    public async Task<Result> DeleteJobImageAsync(
        Guid jobId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        var access = await GetAccessibleJobAsync(jobId, cancellationToken);
        if (!access.IsSuccess)
        {
            return MapFailure(access.Status);
        }

        await storage.DeleteJobImageAsync(
            access.Value.OrganizationId,
            jobId,
            imageId,
            cancellationToken);

        return Result.NoContent();
    }

    public async Task<Result> UploadCurrentProfileImageAsync(
        ImageUpload upload,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        var userId = currentUser.UserId;
        if (organizationId is null || userId is null)
        {
            return Result.Unauthorized();
        }

        var validated = await ValidateAndBufferAsync(upload, cancellationToken);
        if (!validated.IsSuccess)
        {
            return Result.Invalid(validated.ValidationErrors);
        }

        await using var content = validated.Value.Content;
        await storage.UploadProfileImageAsync(
            organizationId.Value,
            userId.Value,
            content,
            validated.Value.ContentType,
            cancellationToken);

        return Result.Success();
    }

    public async Task<Result<ImageFileResponse>> GetProfileImageAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<ImageFileResponse>.Unauthorized();
        }

        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || user.OrganizationId != organizationId.Value)
        {
            return Result<ImageFileResponse>.NotFound();
        }

        var image = await storage.GetProfileImageAsync(
            organizationId.Value,
            userId,
            cancellationToken);

        return image is null
            ? Result<ImageFileResponse>.NotFound()
            : Result<ImageFileResponse>.Success(image);
    }

    public async Task<Result> DeleteCurrentProfileImageAsync(
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        var userId = currentUser.UserId;
        if (organizationId is null || userId is null)
        {
            return Result.Unauthorized();
        }

        await storage.DeleteProfileImageAsync(
            organizationId.Value,
            userId.Value,
            cancellationToken);

        return Result.NoContent();
    }

    private async Task<Result<JobReportResponse>> GetAccessibleJobAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportResponse>.Unauthorized();
        }

        var job = await jobs.GetSingleJobAsync(jobId, organizationId.Value, cancellationToken);
        if (job is null)
        {
            return Result<JobReportResponse>.NotFound();
        }

        if (AuditorDataScope.AppliesTo(currentUser.Role) && !AuditorDataScope.CanAccess(job))
        {
            return Result<JobReportResponse>.NotFound();
        }

        return Result<JobReportResponse>.Success(job);
    }

    private static async Task<Result<ValidatedImage>> ValidateAndBufferAsync(
        ImageUpload upload,
        CancellationToken cancellationToken)
    {
        if (upload.Length <= 0)
        {
            return InvalidImage("Billedfilen er tom.");
        }

        if (upload.Length > MaxImageSizeBytes)
        {
            return InvalidImage("Billedfilen må højst fylde 10 MB.");
        }

        var declaredContentType = NormalizeContentType(upload.ContentType);
        if (declaredContentType is null)
        {
            return InvalidImage("Kun JPEG, PNG og WebP billeder understøttes.");
        }

        var buffer = new MemoryStream((int)Math.Min(upload.Length, MaxImageSizeBytes));
        var copyBuffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await upload.Content.ReadAsync(copyBuffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
            if (totalBytes > MaxImageSizeBytes)
            {
                await buffer.DisposeAsync();
                return InvalidImage("Billedfilen må højst fylde 10 MB.");
            }

            await buffer.WriteAsync(copyBuffer.AsMemory(0, bytesRead), cancellationToken);
        }

        if (totalBytes == 0)
        {
            await buffer.DisposeAsync();
            return InvalidImage("Billedfilen er tom.");
        }

        var detectedContentType = DetectContentType(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        if (detectedContentType is null || !string.Equals(declaredContentType, detectedContentType, StringComparison.Ordinal))
        {
            await buffer.DisposeAsync();
            return InvalidImage("Billedets filtype matcher ikke indholdet.");
        }

        buffer.Position = 0;
        return Result<ValidatedImage>.Success(new ValidatedImage(buffer, detectedContentType));
    }

    private static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var normalized = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return normalized switch
        {
            "image/jpeg" or "image/jpg" => "image/jpeg",
            "image/png" => "image/png",
            "image/webp" => "image/webp",
            _ => null
        };
    }

    private static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8
            && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    private static Result<ValidatedImage> InvalidImage(string message) =>
        Result<ValidatedImage>.Invalid(new ValidationError
        {
            Identifier = "file",
            ErrorMessage = message
        });

    private static Result<T> MapFailure<T>(ResultStatus status) => status switch
    {
        ResultStatus.Unauthorized => Result<T>.Unauthorized(),
        ResultStatus.Forbidden => Result<T>.Forbidden(),
        _ => Result<T>.NotFound()
    };

    private static Result MapFailure(ResultStatus status) => status switch
    {
        ResultStatus.Unauthorized => Result.Unauthorized(),
        ResultStatus.Forbidden => Result.Forbidden(),
        _ => Result.NotFound()
    };

    private sealed record ValidatedImage(MemoryStream Content, string ContentType);
}
