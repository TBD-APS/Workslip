using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Workslip.Application.Documents;
using Workslip.Application.Integrations;
using Workslip.Application.Organizations;
using Workslip.Domain.Models;

namespace Workslip.Tests.Application.Integrations;

public sealed class AccountingIntegrationTests
{
    private IServiceProvider CreateServiceProvider(params IAccountingProvider[] providers)
    {
        var services = new ServiceCollection();
        foreach (var p in providers)
        {
            services.AddSingleton<IAccountingProvider>(p);
        }
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task IntegrationEngine_ReturnsCorrectProvider_WhenOrganizationHasSetProvider()
    {
        // Arrange
        var tenantId = Guid.NewGuid().ToString();
        var providerId = "economics";
        var orgRepository = new FakeOrganizationRepository
        {
            AccountingProviderId = providerId
        };
        var mockProvider = new FakeAccountingProvider("mock");
        var economicsProvider = new FakeAccountingProvider("economics");
        var serviceProvider = CreateServiceProvider(mockProvider, economicsProvider);
        var engine = new IntegrationEngine(serviceProvider, orgRepository);

        // Act
        var provider = await engine.GetAccountingProviderAsync(tenantId);

        // Assert
        Assert.Equal(economicsProvider, provider);
    }

    [Fact]
    public async Task IntegrationEngine_ReturnsFallbackProvider_WhenOrganizationHasNoProviderSet()
    {
        // Arrange
        var tenantId = Guid.NewGuid().ToString();
        var orgRepository = new FakeOrganizationRepository
        {
            AccountingProviderId = null
        };
        var mockProvider = new FakeAccountingProvider("mock");
        var serviceProvider = CreateServiceProvider(mockProvider);
        var engine = new IntegrationEngine(serviceProvider, orgRepository);

        // Act
        var provider = await engine.GetAccountingProviderAsync(tenantId);

        // Assert
        Assert.Equal(mockProvider, provider);
    }

    [Fact]
    public async Task DocumentSyncService_DoesNotDuplicate_WhenDocumentAlreadyMirrored()
    {
        // Arrange
        var tenantId = Guid.NewGuid().ToString();
        var extId = "ext-123";
        var provider = new FakeAccountingProvider("mock");
        var orgRepository = new FakeOrganizationRepository();
        var serviceProvider = CreateServiceProvider(provider);
        var engine = new IntegrationEngine(serviceProvider, orgRepository);
        
        var docRepository = new FakeDocumentRepository();
        var attachmentRepository = new FakeDocumentAttachmentRepository();
        var attachmentStorage = new FakeDocumentAttachmentStorage();
        
        var existingDocId = Guid.NewGuid();
        docRepository.MirroredDocuments.Add(new DocumentDetailResponse(
            existingDocId, "Title", "Content", new[] { $"ext-doc:{extId}" }, 
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null, null, 1));
        
        var service = new DocumentSyncService(engine, docRepository, attachmentRepository, attachmentStorage);

        // Act
        var result = await service.SyncExternalDocumentAsync(tenantId, extId, "test.pdf", "application/pdf", CancellationToken.None);

        // Assert
        Assert.Equal(existingDocId, result);
        Assert.Equal(0, docRepository.CreateCalls);
    }

    [Fact]
    public async Task DocumentSyncService_CreatesNewMirror_WhenDocumentIsNotMirrored()
    {
        // Arrange
        var tenantId = Guid.NewGuid().ToString();
        var extId = "ext-123";
        var provider = new FakeAccountingProvider("mock");
        var orgRepository = new FakeOrganizationRepository();
        var serviceProvider = CreateServiceProvider(provider);
        var engine = new IntegrationEngine(serviceProvider, orgRepository);
        
        var docRepository = new FakeDocumentRepository();
        var attachmentRepository = new FakeDocumentAttachmentRepository();
        var attachmentStorage = new FakeDocumentAttachmentStorage();
        
        var service = new DocumentSyncService(engine, docRepository, attachmentRepository, attachmentStorage);

        // Act
        var result = await service.SyncExternalDocumentAsync(tenantId, extId, "test.pdf", "application/pdf", CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, result);
        Assert.Equal(1, docRepository.CreateCalls);
        Assert.Equal(1, attachmentRepository.CreateCalls);
        Assert.Equal(1, attachmentStorage.UploadCalls);
    }

    // --- Fakes ---

    private sealed class FakeOrganizationRepository : IOrganizationRepository
    {
        public string? AccountingProviderId { get; set; }
        public Task<string?> GetAccountingProviderIdAsync(Guid organizationId, CancellationToken ct) => Task.FromResult(AccountingProviderId);
        public Task<bool> CvrExistsAsync(string n, CancellationToken ct) => Task.FromResult(false);
        public Task<OrganizationRow?> GetByIdAsync(Guid id, CancellationToken ct)
        {
            return Task.FromResult<OrganizationRow?>(// Simulate basic row return
                new OrganizationRow { Id = id, AccountingProviderId = AccountingProviderId });
        }
        public Task<OrganizationOnboardingResponse?> CreateAsync(CreateOrganizationRequest r, string n, CancellationToken ct) => Task.FromResult<OrganizationOnboardingResponse?>(null);
        public Task<CurrentUserResponse?> GetCurrentUserAsync(Guid u, CancellationToken ct) => Task.FromResult<CurrentUserResponse?>(null);
    }

    private sealed class FakeAccountingProvider : IAccountingProvider
    {
        public string ProviderId { get; }
        public string DisplayName => "Fake";
        public FakeAccountingProvider(string id) => ProviderId = id;
        public Task<IEnumerable<AccountingDocument>> GetDocumentsForUserAsync(string t, string u, string s, string e) => Task.FromResult(Enumerable.Empty<AccountingDocument>());
        public Task<Stream> GetDocumentStreamAsync(string t, string d) => Task.FromResult<Stream>(new MemoryStream());
        public Task<bool> SyncHoursAsync(string t, object h) => Task.FromResult(true);
        public Task<bool> TestConnectionAsync(string t) => Task.FromResult(true);
    }

    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        public List<DocumentDetailResponse> MirroredDocuments = new();
        public int CreateCalls { get; private set; }
        
        public Task<IReadOnlyList<DocumentListItemResponse>> ListAsync(Guid o, int l, int off, string? s, CancellationToken ct) => 
            Task.FromResult<IReadOnlyList<DocumentListItemResponse>>(MirroredDocuments.Select(d => new DocumentListItemResponse(d.Id, d.Title, "", d.Tags, d.UpdatedAt, "", 0)).ToList());
        
        public Task<int> CountAsync(Guid o, string? s, CancellationToken ct) => Task.FromResult(MirroredDocuments.Count);
        
        public Task<DocumentDetailResponse?> GetByIdAsync(Guid o, Guid id, CancellationToken ct) => 
            Task.FromResult(MirroredDocuments.FirstOrDefault(d => d.Id == id));
        
        public Task<DocumentDetailResponse> CreateAsync(Guid o, Guid? p, DocumentWriteData d, CancellationToken ct) 
        { 
            CreateCalls++; 
            var doc = new DocumentDetailResponse(Guid.NewGuid(), d.Title, d.Content, d.Tags, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null, null, 1);
            MirroredDocuments.Add(doc);
            return Task.FromResult(doc); 
        }
        
        public Task<DocumentDetailResponse?> UpdateAsync(Guid o, Guid id, Guid? p, DocumentWriteData d, long r, CancellationToken ct) => Task.FromResult<DocumentDetailResponse?>(null);
        public Task<bool> DeleteAsync(Guid o, Guid id, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class FakeDocumentAttachmentRepository : IDocumentAttachmentRepository
    {
        public int CreateCalls { get; private set; }
        public Task<IReadOnlyList<DocumentAttachmentInfoResponse>> ListAsync(Guid o, Guid d, CancellationToken ct) => Task.FromResult<IReadOnlyList<DocumentAttachmentInfoResponse>>([]);
        public Task<DocumentAttachmentInfoResponse?> GetAsync(Guid o, Guid d, Guid a, CancellationToken ct) => Task.FromResult<DocumentAttachmentInfoResponse?>(null);
        public Task<DocumentAttachmentInfoResponse> CreateAsync(Guid o, Guid d, Guid a, string n, string c, long s, Guid? p, CancellationToken ct) 
        { 
            CreateCalls++; 
            return Task.FromResult(new DocumentAttachmentInfoResponse(a, d, n, c, s, DateTimeOffset.UtcNow, null, null)); 
        }
        public Task<bool> DeleteAsync(Guid o, Guid d, Guid a, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class FakeDocumentAttachmentStorage : IDocumentAttachmentStorage
    {
        public int UploadCalls { get; private set; }
        public Task UploadAsync(Guid o, Guid d, Guid a, Stream s, string c, CancellationToken ct) { UploadCalls++; return Task.CompletedTask; }
        public Task<DocumentAttachmentStoredFile?> GetAsync(Guid o, Guid d, Guid a, CancellationToken ct) => Task.FromResult<DocumentAttachmentStoredFile?>(null);
        public Task DeleteAsync(Guid o, Guid d, Guid a, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteDocumentAsync(Guid o, Guid d, CancellationToken ct) => Task.CompletedTask;
    }
}
