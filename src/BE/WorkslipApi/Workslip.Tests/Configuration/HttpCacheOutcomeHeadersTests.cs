using Microsoft.AspNetCore.Http;
using Workslip.Api.Endpoints;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class HttpCacheOutcomeHeadersTests
{
    [Fact]
    public void SetNoStore_marks_response_as_cache_bypass()
    {
        var context = new DefaultHttpContext();

        HttpCacheHeaders.SetNoStore(context);

        Assert.Equal("bypass", context.Response.Headers["X-Workslip-Cache"].ToString());
    }

    [Fact]
    public void Missing_validator_marks_response_as_cache_miss()
    {
        var context = new DefaultHttpContext();

        var matches = HttpCacheHeaders.MatchesIfNoneMatch(context, "W/\"abc\"");

        Assert.False(matches);
        Assert.Equal("miss", context.Response.Headers["X-Workslip-Cache"].ToString());
    }

    [Fact]
    public void Matching_validator_marks_response_as_revalidated()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.IfNoneMatch = "W/\"abc\"";

        var matches = HttpCacheHeaders.MatchesIfNoneMatch(context, "W/\"abc\"");

        Assert.True(matches);
        Assert.Equal("revalidated", context.Response.Headers["X-Workslip-Cache"].ToString());
    }
}
