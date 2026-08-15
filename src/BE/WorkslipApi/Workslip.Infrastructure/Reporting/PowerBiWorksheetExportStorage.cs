using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Workslip.Infrastructure.Reporting;

public interface IPowerBiWorksheetExportStorage
{
    Task UploadAsync(byte[] content, DateTimeOffset exportedAtUtc, CancellationToken cancellationToken);
}

public sealed class AzureBlobPowerBiWorksheetExportStorage : IPowerBiWorksheetExportStorage
{
    private const string BlobName = "worksheets.csv";
    private readonly BlobContainerClient _container;

    public AzureBlobPowerBiWorksheetExportStorage(
        IConfiguration configuration,
        TokenCredential credential,
        IOptions<PowerBiExportOptions> options)
    {
        var accountName = configuration["Azure:DocumentFileStorage:StorageAccountName"];
        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new InvalidOperationException(
                "Azure:DocumentFileStorage:StorageAccountName is required for Power BI exports.");
        }

        var containerName = options.Value.ContainerName.Trim();
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new InvalidOperationException("PowerBiExport:ContainerName is required.");
        }

        _container = new BlobContainerClient(
            new Uri($"https://{accountName}.blob.core.windows.net/{containerName}"),
            credential);
    }

    public async Task UploadAsync(
        byte[] content,
        DateTimeOffset exportedAtUtc,
        CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(BlobName);
        await using var stream = new MemoryStream(content, writable: false);
        await blob.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "text/csv; charset=utf-8",
                    CacheControl = "no-store"
                },
                Metadata = new Dictionary<string, string>
                {
                    ["schema-version"] = "1",
                    ["exported-at-utc"] = exportedAtUtc.UtcDateTime.ToString("O")
                }
            },
            cancellationToken);
    }
}
