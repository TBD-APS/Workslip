using Workslip.Application.Documents;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class DocumentAttachmentSizeLimitMigrationTests
{
    private const string MigrationId = "20260816_1810_wor502_document_attachment_size_limit";

    [Fact]
    public void Migration_UsesTheSame75MbLimitAsTheApplication()
    {
        var sql = File.ReadAllText(FindMigrationPath());
        var expectedBytes = DocumentAttachmentService.MaxAttachmentSizeBytes.ToString();

        Assert.Equal(75, DocumentAttachmentService.MaxAttachmentSizeMegabytes);
        Assert.Contains("CK_KnowledgeDocumentAttachments_Size", sql, StringComparison.Ordinal);
        Assert.Contains($"SizeBytes <= {expectedBytes}", sql, StringComparison.Ordinal);
    }

    private static string FindMigrationPath()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "BE",
                "infrastructure",
                "database",
                "migrations",
                $"{MigrationId}.sql");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate migration {MigrationId}.sql.");
    }
}
