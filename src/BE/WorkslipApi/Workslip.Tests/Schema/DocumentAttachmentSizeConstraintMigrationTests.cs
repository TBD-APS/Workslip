using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Workslip.Tests.Schema;

public sealed class DocumentAttachmentSizeConstraintMigrationTests
{
    private const long MaxAttachmentSizeBytes = 75L * 1024L * 1024L;

    [Fact]
    public void Wor647_migration_widens_named_attachment_size_constraint_to_75_mib()
    {
        var migrationsDirectory = FindMigrationsDirectory();
        var migrationPath = Directory
            .GetFiles(migrationsDirectory, "*wor647*document_attachment*75mb*constraint.sql")
            .Single();
        var sql = File.ReadAllText(migrationPath);

        Assert.Contains("CK_KnowledgeDocumentAttachments_Size", sql, StringComparison.Ordinal);
        Assert.Contains($"SizeBytes <= {MaxAttachmentSizeBytes}", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeBytes <= 20971520", sql, StringComparison.Ordinal);
    }

    private static string FindMigrationsDirectory()
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
                "migrations");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/BE/infrastructure/database/migrations by walking up from "
            + AppContext.BaseDirectory);
    }
}
