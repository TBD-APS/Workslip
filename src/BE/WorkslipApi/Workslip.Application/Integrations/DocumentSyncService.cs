using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workslip.Application.Documents;
using Workslip.Application.Integrations;

namespace Workslip.Application.Integrations;

public interface IDocumentSyncService
{
    Task<Guid> SyncExternalDocumentAsync(
        string tenantId,
        string externalDocumentId,
        string fileName,
        string contentType,
        string externalLink,
        CancellationToken cancellationToken);
}

public class DocumentSyncService(
    IIntegrationEngine integrationEngine,
    IDocumentRepository documentRepository,
    IDocumentAttachmentRepository attachmentRepository,
    IDocumentAttachmentStorage attachmentStorage) : IDocumentSyncService
{
    public async Task<Guid> SyncExternalDocumentAsync(
        string tenantId,
        string externalDocumentId,
        string fileName,
        string contentType,
        string externalLink,
        CancellationToken cancellationToken)
    {
        var organizationId = Guid.Parse(tenantId);

        // 1. Check if we already have a mirrored document for this external ID.
        // We search for a document with a tag identifying the external doc.
        var documents = await documentRepository.ListAsync(organizationId, 100, 0, $"ext-doc:{externalDocumentId}", cancellationToken);
        var existingDoc = documents.FirstOrDefault();
        if (existingDoc != null)
        {
            return existingDoc.Id;
        }

        // 2. Mirror the document.
        var provider = await integrationEngine.GetAccountingProviderAsync(tenantId);
        using var stream = await provider.GetDocumentStreamAsync(tenantId, externalDocumentId);
        if (stream == null)
        {
            throw new InvalidOperationException($"Could not fetch document {externalDocumentId} from provider {provider.ProviderId}.");
        }

        // Create a shell document in our internal system.
        var docResponse = await documentRepository.CreateAsync(
            organizationId,
            null,
            new DocumentWriteData(
                Title: $"Mirrored: {fileName}",
                Content: $"External reference: {externalDocumentId}\nExternal link: {externalLink}",
                Tags: new[] { $"ext-doc:{externalDocumentId}", $"ext-url:{externalLink}", "mirrored" }),
            cancellationToken);

        // Create attachment metadata.
        var attachmentId = Guid.NewGuid();
        await attachmentRepository.CreateAsync(
            organizationId,
            docResponse.Id,
            attachmentId,
            fileName,
            contentType,
            stream.Length,
            null,
            cancellationToken);

        // Store the actual file bytes.
        await attachmentStorage.UploadAsync(
            organizationId,
            docResponse.Id,
            attachmentId,
            stream,
            contentType,
            cancellationToken);

        return docResponse.Id;
    }
}
