using System;
using System.IO;
using Xunit;

namespace Workslip.Tests.Conversations;

public sealed class ConversationDelegatedAuthorContractTests
{
    [Fact]
    public void Conversation_author_foreign_key_allows_delegated_superadmin_actor()
    {
        var root = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(
            root,
            "src",
            "BE",
            "infrastructure",
            "database",
            "migrations",
            "20260824_0010_wor765_conversation_superadmin_author.sql"));

        Assert.Contains("FOREIGN KEY (AuthorUserId)", migration, StringComparison.Ordinal);
        Assert.Contains("REFERENCES dbo.Users (Id)", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("FOREIGN KEY (OrganizationId, AuthorUserId)", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void Conversation_repository_resolves_author_by_global_user_id()
    {
        var root = FindRepositoryRoot();
        var repository = File.ReadAllText(Path.Combine(
            root,
            "src",
            "BE",
            "WorkslipApi",
            "Workslip.Infrastructure",
            "Repositories",
            "SqlJobConversationRepository.cs"));

        Assert.Contains("ON author.Id = m.AuthorUserId", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("author.OrganizationId = m.OrganizationId", repository, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "BE", "WorkslipApi")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the Workslip repository root by walking up from "
            + AppContext.BaseDirectory);
    }
}
