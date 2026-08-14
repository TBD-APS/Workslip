using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Workslip.Application.Images;

namespace Workslip.Infrastructure.Storage;

public sealed class LocalImageStorage : IImageStorage
{
    private readonly string _rootPath;

    public LocalImageStorage(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var configuredRoot = configuration["Azure:DocumentFileStorage:LocalRootPath"];
        configuredRoot = string.IsNullOrWhiteSpace(configuredRoot) ? "UploadedFiles" : configuredRoot;
        _rootPath = Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot);
    }

    public async Task<IReadOnlyList<ImageInfoResponse>> ListJobImagesAsync(
        Guid organizationId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var directory = JobImageDirectory(organizationId, jobId);
        if (!Directory.Exists(directory))
        {
            return Array.Empty<ImageInfoResponse>();
        }

        var images = new List<ImageInfoResponse>();
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Guid.TryParseExact(Path.GetFileName(path), "N", out var imageId))
            {
                continue;
            }

            var contentType = await DetectContentTypeAsync(path, cancellationToken);
            if (contentType is null)
            {
                continue;
            }

            var info = new FileInfo(path);
            images.Add(new ImageInfoResponse(
                imageId,
                contentType,
                info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc)));
        }

        return images
            .OrderBy(image => image.CreatedAt)
            .ToArray();
    }

    public async Task<ImageInfoResponse> UploadJobImageAsync(
        Guid organizationId,
        Guid jobId,
        Guid imageId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var path = JobImagePath(organizationId, jobId, imageId);
        await WriteFileAsync(path, content, cancellationToken);
        var info = new FileInfo(path);

        return new ImageInfoResponse(
            imageId,
            contentType,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc));
    }

    public Task<ImageFileResponse?> GetJobImageAsync(
        Guid organizationId,
        Guid jobId,
        Guid imageId,
        CancellationToken cancellationToken) =>
        GetFileAsync(JobImagePath(organizationId, jobId, imageId), cancellationToken);

    public Task DeleteJobImageAsync(
        Guid organizationId,
        Guid jobId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = JobImagePath(organizationId, jobId, imageId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task DeleteJobImagesAsync(
        Guid organizationId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = JobImageDirectory(organizationId, jobId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public Task UploadProfileImageAsync(
        Guid organizationId,
        Guid userId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken) =>
        WriteFileAsync(ProfileImagePath(organizationId, userId), content, cancellationToken);

    public Task<ImageFileResponse?> GetProfileImageAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        GetFileAsync(ProfileImagePath(organizationId, userId), cancellationToken);

    public Task DeleteProfileImageAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ProfileImagePath(organizationId, userId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static async Task WriteFileAsync(
        string path,
        Stream content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            {
                await content.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<ImageFileResponse?> GetFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var contentType = await DetectContentTypeAsync(path, cancellationToken);
        if (contentType is null)
        {
            return null;
        }

        try
        {
            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return new ImageFileResponse(stream, contentType, stream.Length);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static async Task<string?> DetectContentTypeAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var header = new byte[12];
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                header.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var bytesRead = await stream.ReadAsync(header, cancellationToken);
            return DetectContentType(header.AsSpan(0, bytesRead));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
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

    private string JobImageDirectory(Guid organizationId, Guid jobId) =>
        Path.Combine(
            _rootPath,
            "organizations",
            organizationId.ToString("N"),
            "jobs",
            jobId.ToString("N"),
            "images");

    private string JobImagePath(Guid organizationId, Guid jobId, Guid imageId) =>
        Path.Combine(JobImageDirectory(organizationId, jobId), imageId.ToString("N"));

    private string ProfileImagePath(Guid organizationId, Guid userId) =>
        Path.Combine(
            _rootPath,
            "organizations",
            organizationId.ToString("N"),
            "users",
            userId.ToString("N"),
            "profile");
}
