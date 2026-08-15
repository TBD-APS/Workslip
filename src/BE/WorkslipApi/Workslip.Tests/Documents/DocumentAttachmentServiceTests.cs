using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Documents;
using Xunit;

namespace Workslip.Tests.Documents;

public sealed class DocumentAttachmentServiceTests
{
    [Fact]
    public async Task Upload_without_organization_fails_closed()
    {
        var docs = new DocumentRepositoryStub { ExistingDocument = CreateDocument() };
        var attachments = new AttachmentRepositoryStub();
        var storage = new AttachmentStorageStub();
        var service = CreateService(docs, attachments, storage, new TestCurrentUserContext(Guid.NewGuid(), null, "Admin"));

        await using var content = new MemoryStream([0x49, 0x44, 0x33]);
        var result = await service.UploadAsync(
            Guid.NewGuid(),
            new DocumentAttachmentUpload(content, content.Length, "success.mp3", "audio/mpeg"),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Equal(0, storage.UploadCount);
        Assert.Equal(0, attachments.CreateCount);
    }

    [Fact]
    public async Task Upload_rejects_unsafe_file_type_before_storage()
    {
        var organizationId = Guid.NewGuid();
        var document = CreateDocument();
        var docs = new DocumentRepositoryStub { ExistingDocument = document };
        var attachments = new AttachmentRepositoryStub();
        var storage = new AttachmentStorageStub();
        var service = CreateService(docs, attachments, storage, new TestCurrentUserContext(Guid.NewGuid(), organizationId, "Admin"));

        await using var content = new MemoryStream("<html>unsafe</html>"u8.ToArray());
        var result = await service.UploadAsync(
            document.Id,
            new DocumentAttachmentUpload(content, content.Length, "payload.html", "text/html"),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(0, storage.UploadCount);
        Assert.Equal(0, attachments.CreateCount);
    }

    [Fact]
    public async Task Upload_mp3_uses_current_tenant_and_persists_metadata()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var document = CreateDocument();
        var docs = new DocumentRepositoryStub { ExistingDocument = document };
        var attachments = new AttachmentRepositoryStub();
        var storage = new AttachmentStorageStub();
        var service = CreateService(docs, attachments, storage, new TestCurrentUserContext(userId, organizationId, "Admin"));

        await using var content = new MemoryStream([0x49, 0x44, 0x33, 0x04]);
        var result = await service.UploadAsync(
            document.Id,
            new DocumentAttachmentUpload(content, content.Length, "  completion.mp3  ", "audio/mpeg"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, storage.UploadCount);
        Assert.Equal(organizationId, storage.LastOrganizationId);
        Assert.Equal(document.Id, storage.LastDocumentId);
        Assert.Equal("audio/mpeg", storage.LastContentType);
        Assert.Equal(1, attachments.CreateCount);
        Assert.Equal(organizationId, attachments.LastOrganizationId);
        Assert.Equal(document.Id, attachments.LastDocumentId);
        Assert.Equal("completion.mp3", attachments.LastFileName);
        Assert.Equal(userId, attachments.LastActorUserId);
    }

    [Fact]
    public async Task Upload_cleans_blob_when_metadata_write_fails()
    {
        var organizationId = Guid.NewGuid();
        var document = CreateDocument();
        var docs = new DocumentRepositoryStub { ExistingDocument = document };
        var attachments = new AttachmentRepositoryStub { ThrowOnCreate = true };
        var storage = new AttachmentStorageStub();
        var service = CreateService(docs, attachments, storage, new TestCurrentUserContext(Guid.NewGuid(), organizationId, "Admin"));

        await using var content = new MemoryStream([0x49, 0x44, 0x33, 0x04]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadAsync(
            document.Id,
            new DocumentAttachmentUpload(content, content.Length, "completion.mp3", "audio/mpeg"),
            CancellationToken.None));

        Assert.Equal(1, storage.UploadCount);
        Assert.Equal(1, storage.DeleteCount);
    }

    [Fact]
    public async Task Get_does_not_read_blob_when_tenant_scoped_metadata_is_missing()
    {
        var organizationId = Guid.NewGuid();
        var document = CreateDocument();
        var docs = new DocumentRepositoryStub { ExistingDocument = document };
        var attachments = new AttachmentRepositoryStub();
        var storage = new AttachmentStorageStub();
        var service = CreateService(docs, attachments, storage, new TestCurrentUserContext(Guid.NewGuid(), organizationId, "User"));

        var result = await service.GetAsync(document.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(0, storage.GetCount);
        Assert.Equal(organizationId, attachments.LastOrganizationId);
    }

    private static DocumentAttachmentService CreateService(
        IDocumentRepository docs,
        IDocumentAttachmentRepository attachments,
        IDocumentAttachmentStorage storage,
        ICurrentUserContext currentUser) =>
        new(docs, attachments, storage, currentUser, NullLogger<DocumentAttachmentService>.Instance);

    private static DocumentDetailResponse CreateDocument()
    {
        var now = DateTimeOffset.UtcNow;
        return new DocumentDetailResponse(
            Guid.NewGuid(),
            "Test",
            "Content",
            [],
            now,
            now,
            null,
            null,
            null,
            null,
            1);
    }

    private sealed record TestCurrentUserContext(Guid? UserId, Guid? OrganizationId, string? Role) : ICurrentUserContext;

    private sealed class DocumentRepositoryStub : IDocumentRepository
    {
        public DocumentDetailResponse? ExistingDocument { get; init; }
        public Guid? LastOrganizationId { get; private set; }

        public Task<DocumentDetailResponse?> GetByIdAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
        {
            LastOrganizationId = organizationId;
            return Task.FromResult(ExistingDocument is not null && ExistingDocument.Id == id ? ExistingDocument : null);
        }

        public Task<IReadOnlyList<DocumentListItemResponse>> ListAsync(Guid organizationId, int limit, int offset, string? search, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentListItemResponse>>([]);

        public Task<int> CountAsync(Guid organizationId, string? search, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<DocumentDetailResponse> CreateAsync(Guid organizationId, Guid? actorUserId, DocumentWriteData document, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DocumentDetailResponse?> UpdateAsync(Guid organizationId, Guid id, Guid? actorUserId, DocumentWriteData document, long expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class AttachmentRepositoryStub : IDocumentAttachmentRepository
    {
        public int CreateCount { get; private set; }
        public bool ThrowOnCreate { get; init; }
        public Guid? LastOrganizationId { get; private set; }
        public Guid? LastDocumentId { get; private set; }
        public string? LastFileName { get; private set; }
        public Guid? LastActorUserId { get; private set; }

        public Task<IReadOnlyList<DocumentAttachmentInfoResponse>> ListAsync(Guid organizationId, Guid documentId, CancellationToken cancellationToken)
        {
            LastOrganizationId = organizationId;
            LastDocumentId = documentId;
            return Task.FromResult<IReadOnlyList<DocumentAttachmentInfoResponse>>([]);
        }

        public Task<DocumentAttachmentInfoResponse?> GetAsync(Guid organizationId, Guid documentId, Guid attachmentId, CancellationToken cancellationToken)
        {
            LastOrganizationId = organizationId;
            LastDocumentId = documentId;
            return Task.FromResult<DocumentAttachmentInfoResponse?>(null);
        }

        public Task<DocumentAttachmentInfoResponse> CreateAsync(Guid organizationId, Guid documentId, Guid attachmentId, string fileName, string contentType, long sizeBytes, Guid? actorUserId, CancellationToken cancellationToken)
        {
            CreateCount++;
            LastOrganizationId = organizationId;
            LastDocumentId = documentId;
            LastFileName = fileName;
            LastActorUserId = actorUserId;
            if (ThrowOnCreate)
                throw new InvalidOperationException("metadata failure");

            return Task.FromResult(new DocumentAttachmentInfoResponse(
                attachmentId,
                documentId,
                fileName,
                contentType,
                sizeBytes,
                DateTimeOffset.UtcNow,
                actorUserId,
                null));
        }

        public Task<bool> DeleteAsync(Guid organizationId, Guid documentId, Guid attachmentId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class AttachmentStorageStub : IDocumentAttachmentStorage
    {
        public int UploadCount { get; private set; }
        public int GetCount { get; private set; }
        public int DeleteCount { get; private set; }
        public Guid? LastOrganizationId { get; private set; }
        public Guid? LastDocumentId { get; private set; }
        public string? LastContentType { get; private set; }

        public Task UploadAsync(Guid organizationId, Guid documentId, Guid attachmentId, Stream content, string contentType, CancellationToken cancellationToken)
        {
            UploadCount++;
            LastOrganizationId = organizationId;
            LastDocumentId = documentId;
            LastContentType = contentType;
            return Task.CompletedTask;
        }

        public Task<DocumentAttachmentStoredFile?> GetAsync(Guid organizationId, Guid documentId, Guid attachmentId, CancellationToken cancellationToken)
        {
            GetCount++;
            return Task.FromResult<DocumentAttachmentStoredFile?>(null);
        }

        public Task DeleteAsync(Guid organizationId, Guid documentId, Guid attachmentId, CancellationToken cancellationToken)
        {
            DeleteCount++;
            return Task.CompletedTask;
        }

        public Task DeleteDocumentAsync(Guid organizationId, Guid documentId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
