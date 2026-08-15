using System.Data;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Documents;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Documents;

public sealed class SqlDocumentRepositoryTenantIsolationTests
{
    static SqlDocumentRepositoryTenantIsolationTests()
    {
        // SQL Server returns native Guid/DateTimeOffset values. SQLite's test
        // provider materializes them as text; handlers keep this relational
        // regression test focused on Workslip's SQL predicates/concurrency.
        SqlMapper.AddTypeHandler(new GuidValueHandler());
        SqlMapper.AddTypeHandler(new DateTimeOffsetValueHandler());
    }

    [Fact]
    public async Task Document_operations_stay_tenant_scoped_and_reject_stale_revision()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await CreateSchemaAsync(connection);

        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new SqlDbContext(options);
        var repository = new SqlDocumentRepository(dbContext, new NoRetryPolicy());
        var attachmentRepository = new SqlDocumentAttachmentRepository(dbContext, new NoRetryPolicy());

        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var created = await repository.CreateAsync(
            organizationA,
            actor,
            new DocumentWriteData("Driftshåndbog", "Kun tenant A må se dette", ["Drift"]),
            CancellationToken.None);

        Assert.Equal(1, created.Revision);
        Assert.NotNull(await repository.GetByIdAsync(organizationA, created.Id, CancellationToken.None));
        Assert.Null(await repository.GetByIdAsync(organizationB, created.Id, CancellationToken.None));
        Assert.Empty(await repository.ListAsync(organizationB, 50, 0, null, CancellationToken.None));
        Assert.Equal(0, await repository.CountAsync(organizationB, null, CancellationToken.None));

        var attachmentId = Guid.NewGuid();
        var attachment = await attachmentRepository.CreateAsync(
            organizationA,
            created.Id,
            attachmentId,
            "completion.mp3",
            "audio/mpeg",
            2048,
            actor,
            CancellationToken.None);
        Assert.Equal(attachmentId, attachment.Id);
        Assert.Single(await attachmentRepository.ListAsync(organizationA, created.Id, CancellationToken.None));
        Assert.Empty(await attachmentRepository.ListAsync(organizationB, created.Id, CancellationToken.None));
        Assert.Null(await attachmentRepository.GetAsync(organizationB, created.Id, attachmentId, CancellationToken.None));
        Assert.False(await attachmentRepository.DeleteAsync(organizationB, created.Id, attachmentId, CancellationToken.None));

        var crossTenantUpdate = await repository.UpdateAsync(
            organizationB,
            created.Id,
            actor,
            new DocumentWriteData("Lækket", "Må ikke gemmes", []),
            created.Revision,
            CancellationToken.None);
        Assert.Null(crossTenantUpdate);
        Assert.False(await repository.DeleteAsync(organizationB, created.Id, CancellationToken.None));

        var updated = await repository.UpdateAsync(
            organizationA,
            created.Id,
            actor,
            new DocumentWriteData("Driftshåndbog v2", "Opdateret sikkert", ["Drift", "V2"]),
            created.Revision,
            CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(2, updated!.Revision);

        await Assert.ThrowsAsync<DocumentRevisionConflictException>(() => repository.UpdateAsync(
            organizationA,
            created.Id,
            actor,
            new DocumentWriteData("Forældet", "Må ikke overskrive", []),
            created.Revision,
            CancellationToken.None));

        var persisted = await repository.GetByIdAsync(organizationA, created.Id, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal("Driftshåndbog v2", persisted!.Title);
        Assert.Equal("Opdateret sikkert", persisted.Content);
        Assert.Equal(2, persisted.Revision);
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Users
            (
                Id TEXT NOT NULL,
                OrganizationId TEXT NOT NULL,
                DisplayName TEXT NULL,
                PRIMARY KEY (Id)
            );

            CREATE TABLE KnowledgeDocuments
            (
                Id TEXT NOT NULL,
                OrganizationId TEXT NOT NULL,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL,
                TagsJson TEXT NOT NULL,
                CreatedByUserId TEXT NULL,
                UpdatedByUserId TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                Revision INTEGER NOT NULL,
                PRIMARY KEY (Id),
                UNIQUE (OrganizationId, Id)
            );

            CREATE TABLE KnowledgeDocumentAttachments
            (
                Id TEXT NOT NULL,
                OrganizationId TEXT NOT NULL,
                DocumentId TEXT NOT NULL,
                FileName TEXT NOT NULL,
                ContentType TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                UploadedByUserId TEXT NULL,
                CreatedAt TEXT NOT NULL,
                PRIMARY KEY (Id),
                FOREIGN KEY (OrganizationId, DocumentId)
                    REFERENCES KnowledgeDocuments(OrganizationId, Id)
                    ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoRetryPolicy : IDatabaseRetryPolicy
    {
        public Task ExecuteAsync(
            string operationName,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);

        public Task<T> ExecuteAsync<T>(
            string operationName,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
    }

    private sealed class GuidValueHandler : SqlMapper.TypeHandler<Guid>
    {
        public override Guid Parse(object value) => value switch
        {
            Guid guid => guid,
            string text => Guid.Parse(text),
            byte[] bytes when bytes.Length == 16 => new Guid(bytes),
            _ => throw new DataException($"Cannot convert {value.GetType().Name} to Guid.")
        };

        public override void SetValue(IDbDataParameter parameter, Guid value) => parameter.Value = value;
    }

    private sealed class DateTimeOffsetValueHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(dateTime),
            string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            _ => throw new DataException($"Cannot convert {value.GetType().Name} to DateTimeOffset.")
        };

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) => parameter.Value = value;
    }
}
