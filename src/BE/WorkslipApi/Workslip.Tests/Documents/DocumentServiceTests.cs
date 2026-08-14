using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Documents;
using Workslip.Application.Documents.Validators;
using Xunit;

namespace Workslip.Tests.Documents;

public sealed class DocumentServiceTests
{
    [Fact]
    public async Task List_without_organization_fails_closed()
    {
        var repository = new RecordingRepository();
        var service = CreateService(repository, new RecordingAttachmentStorage(), new TestCurrentUserContext(Guid.NewGuid(), null, "Admin"));

        var result = await service.ListAsync(null, null, null, CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Null(repository.LastOrganizationId);
    }

    [Fact]
    public async Task Create_uses_current_tenant_and_normalizes_tags()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new RecordingRepository();
        var service = CreateService(repository, new RecordingAttachmentStorage(), new TestCurrentUserContext(userId, organizationId, "Admin"));

        var result = await service.CreateAsync(
            new CreateDocumentRequest("  Driftshåndbog  ", "Indhold", [" Drift ", "drift", " Vagt "]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(organizationId, repository.LastOrganizationId);
        Assert.Equal(userId, repository.LastActorUserId);
        Assert.NotNull(repository.LastWrite);
        Assert.Equal("Driftshåndbog", repository.LastWrite!.Title);
        Assert.Equal(["Drift", "Vagt"], repository.LastWrite.Tags);
    }

    [Fact]
    public async Task Update_maps_stale_revision_to_conflict()
    {
        var organizationId = Guid.NewGuid();
        var repository = new RecordingRepository { ThrowRevisionConflict = true };
        var service = CreateService(
            repository,
            new RecordingAttachmentStorage(),
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, "Admin"));

        var result = await service.UpdateAsync(
            Guid.NewGuid(),
            new UpdateDocumentRequest("Titel", "Indhold", [], 1),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(organizationId, repository.LastOrganizationId);
    }

    [Fact]
    public async Task Delete_cleans_attachment_storage_after_document_delete()
    {
        var organizationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var repository = new RecordingRepository();
        var storage = new RecordingAttachmentStorage();
        var service = CreateService(
            repository,
            storage,
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, "Admin"));

        var result = await service.DeleteAsync(documentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((organizationId, documentId), storage.LastDeletedDocument);
    }

    private static DocumentService CreateService(
        RecordingRepository repository,
        IDocumentAttachmentStorage attachmentStorage,
        ICurrentUserContext currentUser) =>
        new(
            repository,
            attachmentStorage,
            currentUser,
            new CreateDocumentRequestValidator(),
            new UpdateDocumentRequestValidator(),
            NullLogger<DocumentService>.Instance);

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class RecordingRepository : IDocumentRepository
    {
        public Guid? LastOrganizationId { get; private set; }
        public Guid? LastActorUserId { get; private set; }
        public DocumentWriteData? LastWrite { get; private set; }
        public bool ThrowRevisionConflict { get; init; }

        public Task<IReadOnlyList<DocumentListItemResponse>> ListAsync(Guid organizationId, int limit, int offset, string? search, CancellationToken cancellationToken)
        {
            LastOrganizationId = organizationId;
            return Task.FromResult<IReadOnlyList<DocumentListItemResponse>>([]);
        }

        public Task<int> CountAsync(Guid organizationId, string? search, CancellationToken cancellationToken)
        {
            LastOrganizationId = organizationId;
            return Task.FromResult(0);
        }

        public Task<DocumentDetailResponse?> GetByIdAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
        {
            LastOrganizationId = organizationId;
            return Task.FromResult<DocumentDetailResponse?>(null);
        }

        public Task<DocumentDetailResponse> CreateAsync(Guid organizationId, Guid? actorUserId, DocumentWriteData document, CancellationToken cancellationToken)
        {
            LastOrganizationId = organizationId;
            LastActorUserId = actorUserId;
            LastWrite = document;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new DocumentDetailResponse(
                Guid.NewGuid(), document.Title, document.Content, document.Tags,
                now, now, actorUserId, null, actorUserId, null, 1));
        }

        public Task<DocumentDetailResponse?> UpdateAsync(Guid organizationId, Guid id, Guid? actorUserId, DocumentWriteData document, long expectedRevision, CancellationToken cancellationToken)
        {
            LastOrganizationId = organizationId;
            LastActorUserId = actorUserId;
            LastWrite = document;
            if (ThrowRevisionConflict)
                throw new DocumentRevisionConflictException(id);

            var now = DateTimeOffset.UtcNow;
            return Task.FromResult<DocumentDetailResponse?>(new DocumentDetailResponse(
                id, document.Title, document.Content, document.Tags,
                now, now, actorUserId, null, actorUserId, null, expectedRevision + 1));
        }

        public Task<bool> DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken)
        {
            LastOrganizationId = organizationId;
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingAttachmentStorage : IDocumentAttachmentStorage
    {
        public (Guid OrganizationId, Guid DocumentId)? LastDeletedDocument { get; private set; }

        public Task UploadAsync(Guid organizationId, Guid documentId, Guid attachmentId, Stream content, string contentType, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<DocumentAttachmentStoredFile?> GetAsync(Guid organizationId, Guid documentId, Guid attachmentId, CancellationToken cancellationToken) =>
            Task.FromResult<DocumentAttachmentStoredFile?>(null);

        public Task DeleteAsync(Guid organizationId, Guid documentId, Guid attachmentId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteDocumentAsync(Guid organizationId, Guid documentId, CancellationToken cancellationToken)
        {
            LastDeletedDocument = (organizationId, documentId);
            return Task.CompletedTask;
        }
    }
}
