using Workslip.Application.Documents;

namespace Workslip.Application.Integrations;

public interface IDocumentSyncService
{
    Task<Guid> SyncExternalDocumentAsync(
        string tenantId,
        string externalDocumentId,
        string fileName,
        string contentType,
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
        CancellationToken cancellationToken)
    {
        var organizationId = Guid.Parse(tenantId);

        var documents = await documentRepository.ListAsync(
            organizationId,
            100,
            0,
            $"ext-doc:{externalDocumentId}",
            cancellationToken);
        var existingDoc = documents.FirstOrDefault();
        if (existingDoc is not null)
        {
            return existingDoc.Id;
        }

        var provider = await integrationEngine.GetAccountingProviderAsync(tenantId);
        using var stream = await provider.GetDocumentStreamAsync(tenantId, externalDocumentId);

        var docResponse = await documentRepository.CreateAsync(
            organizationId,
            null,
            new DocumentWriteData(
                Title: $"Mirrored: {fileName}",
                Content: $"External reference: {externalDocumentId}",
                Tags: [$"ext-doc:{externalDocumentId}", "mirrored"]),
            cancellationToken);

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
