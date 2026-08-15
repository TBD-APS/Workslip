using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Workslip.Application.Documents;

namespace Workslip.Infrastructure.Storage;

public sealed class LocalDocumentAttachmentStorage : IDocumentAttachmentStorage
{
    private readonly string _rootPath;

    public LocalDocumentAttachmentStorage(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredRoot = configuration["Azure:DocumentFileStorage:LocalRootPath"];
        configuredRoot = string.IsNullOrWhiteSpace(configuredRoot) ? "UploadedFiles" : configuredRoot;
        _rootPath = Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot);
    }

    public async Task UploadAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var path = AttachmentPath(organizationId, documentId, attachmentId);
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
                File.Delete(temporaryPath);
        }
    }

    public Task<DocumentAttachmentStoredFile?> GetAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = AttachmentPath(organizationId, documentId, attachmentId);
        if (!File.Exists(path))
            return Task.FromResult<DocumentAttachmentStoredFile?>(null);

        try
        {
            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult<DocumentAttachmentStoredFile?>(new DocumentAttachmentStoredFile(stream, stream.Length));
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<DocumentAttachmentStoredFile?>(null);
        }
    }

    public Task DeleteAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = AttachmentPath(organizationId, documentId, attachmentId);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task DeleteDocumentAsync(
        Guid organizationId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = AttachmentDirectory(organizationId, documentId);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        return Task.CompletedTask;
    }

    private string AttachmentDirectory(Guid organizationId, Guid documentId) =>
        Path.Combine(
            _rootPath,
            "organizations",
            organizationId.ToString("N"),
            "docs",
            documentId.ToString("N"),
            "attachments");

    private string AttachmentPath(Guid organizationId, Guid documentId, Guid attachmentId) =>
        Path.Combine(AttachmentDirectory(organizationId, documentId), attachmentId.ToString("N"));
}
