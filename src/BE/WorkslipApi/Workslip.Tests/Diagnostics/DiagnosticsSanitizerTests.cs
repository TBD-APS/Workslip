using Workslip.Infrastructure.Diagnostics;

namespace Workslip.Tests.Diagnostics;

public sealed class DiagnosticsSanitizerTests
{
    [Fact]
    public void SanitizeMessage_RedactsCredentialsPersonalDataAndEntityIds()
    {
        const string input = "authorization: Bearer abc.def.ghi email user@example.com phone +45 12 34 56 78 job 92779e5b-da5b-4cc4-bbeb-07b40cab806f";

        var result = DiagnosticsSanitizer.SanitizeMessage(input);

        Assert.DoesNotContain("abc.def.ghi", result, StringComparison.Ordinal);
        Assert.DoesNotContain("user@example.com", result, StringComparison.Ordinal);
        Assert.DoesNotContain("12 34 56 78", result, StringComparison.Ordinal);
        Assert.DoesNotContain("92779e5b-da5b-4cc4-bbeb-07b40cab806f", result, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result, StringComparison.Ordinal);
        Assert.Contains("[REDACTED_EMAIL]", result, StringComparison.Ordinal);
        Assert.Contains(":id", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeRoute_RemovesQueryAndNormalizesIdentifiers()
    {
        const string input = "/api/jobs/92779e5b-da5b-4cc4-bbeb-07b40cab806f/events/42?access_token=secret";

        var result = DiagnosticsSanitizer.SanitizeRoute(input);

        Assert.Equal("/api/jobs/:id/events/:id", result);
    }

    [Theory]
    [InlineData("00-7b2d7f628e2f4f28a7d7ac889917126b-64d876b29e6b4a52-01")]
    [InlineData("f93a41e5-5457-463b-b7f2-e37ccca69673")]
    public void SanitizeCorrelationId_PreservesHexOperationalIdentifiers(string value)
    {
        Assert.Equal(value, DiagnosticsSanitizer.SanitizeCorrelationId(value));
    }

    [Theory]
    [InlineData("corr_20260802-abc")]
    [InlineData("Bearer.secret-token@example.com")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ1c2VyIn0.signature")]
    public void SanitizeCorrelationId_RejectsArbitraryOrTokenLikeValues(string value)
    {
        Assert.Null(DiagnosticsSanitizer.SanitizeCorrelationId(value));
    }

    [Fact]
    public void Fingerprint_IsStableAndDoesNotContainInput()
    {
        var first = DiagnosticsSanitizer.Fingerprint("backend", "SqlException", "safe message");
        var second = DiagnosticsSanitizer.Fingerprint("backend", "SqlException", "safe message");

        Assert.Equal(first, second);
        Assert.Matches("^[a-f0-9]{12}$", first);
        Assert.DoesNotContain("message", first, StringComparison.OrdinalIgnoreCase);
    }
}
