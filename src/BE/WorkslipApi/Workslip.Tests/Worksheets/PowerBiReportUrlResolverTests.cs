using Workslip.Api.Helpers;
using Xunit;

namespace Workslip.Tests.Worksheets;

public sealed class PowerBiReportUrlResolverTests
{
    private static readonly Guid ReportId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid GroupId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid TenantId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

    [Fact]
    public void Resolve_MyWorkspaceReport_BuildsAuthenticatedEmbedUrlAndKeepsPage()
    {
        var configuredUrl = $"https://app.powerbi.com/groups/me/reports/{ReportId}/ReportSection";

        var result = PowerBiReportUrlResolver.Resolve(configuredUrl);

        Assert.NotNull(result);
        Assert.Equal(configuredUrl, result.Url);
        Assert.Contains($"reportId={ReportId}", result.EmbedUrl);
        Assert.Contains("autoAuth=true", result.EmbedUrl);
        Assert.Contains("pageName=ReportSection", result.EmbedUrl);
    }

    [Fact]
    public void Resolve_WorkspaceReport_PreservesWorkspaceAndTenantCoordinates()
    {
        var configuredUrl = $"https://app.powerbi.com/groups/{GroupId}/reports/{ReportId}/ReportSection42?ctid={TenantId}";

        var result = PowerBiReportUrlResolver.Resolve(configuredUrl);

        Assert.NotNull(result);
        Assert.Contains($"reportId={ReportId}", result.EmbedUrl);
        Assert.Contains("autoAuth=true", result.EmbedUrl);
        Assert.Contains($"groupId={GroupId}", result.EmbedUrl);
        Assert.Contains($"ctid={TenantId}", result.EmbedUrl);
        Assert.Contains("pageName=ReportSection42", result.EmbedUrl);
    }

    [Fact]
    public void Resolve_SecureReportEmbedUrl_NormalizesToApprovedParameters()
    {
        var configuredUrl = $"https://app.powerbi.com/reportEmbed?reportId={ReportId}&groupId={GroupId}&ctid={TenantId}&pageName=ReportSectionA&navContentPaneEnabled=false";

        var result = PowerBiReportUrlResolver.Resolve(configuredUrl);

        Assert.NotNull(result);
        Assert.DoesNotContain("navContentPaneEnabled", result.EmbedUrl);
        Assert.Contains($"reportId={ReportId}", result.EmbedUrl);
        Assert.Contains($"groupId={GroupId}", result.EmbedUrl);
        Assert.Contains($"ctid={TenantId}", result.EmbedUrl);
        Assert.Contains("pageName=ReportSectionA", result.EmbedUrl);
    }

    [Theory]
    [InlineData("https://app.powerbi.com/view?r=public-token")]
    [InlineData("https://app.powerbi.com/groups/me/reports/not-a-guid")]
    [InlineData("https://app.powerbi.com/groups/not-a-guid/reports/11111111-2222-3333-4444-555555555555")]
    [InlineData("https://app.powerbi.com/groups/me/reports/11111111-2222-3333-4444-555555555555?ctid=not-a-guid")]
    [InlineData("https://app.powerbi.com:444/groups/me/reports/11111111-2222-3333-4444-555555555555")]
    [InlineData("https://user@app.powerbi.com/groups/me/reports/11111111-2222-3333-4444-555555555555")]
    [InlineData("https://evil.example.com/groups/me/reports/11111111-2222-3333-4444-555555555555")]
    [InlineData("http://app.powerbi.com/groups/me/reports/11111111-2222-3333-4444-555555555555")]
    [InlineData("")]
    public void Resolve_UnsafeOrUnsupportedUrl_ReturnsNull(string configuredUrl)
    {
        Assert.Null(PowerBiReportUrlResolver.Resolve(configuredUrl));
    }
}
