using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Workslip.Api.Helpers;
using Xunit;

namespace Workslip.Tests.Auth;

public sealed class RefreshSessionCookieTests
{
    [Fact]
    public void Write_InProduction_UsesHostOnlySecureHttpOnlyStrictCookie()
    {
        var context = CreateContext(Environments.Production);

        RefreshSessionCookie.Write(context, "opaque-token", DateTimeOffset.UtcNow.AddDays(14));

        var header = Assert.Single(context.Response.Headers.SetCookie);
        Assert.NotNull(header);
        Assert.StartsWith("__Host-workslip-refresh=", header);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opaque-token", string.Join(' ', context.Response.Headers.Where(x => x.Key != "Set-Cookie")));
    }

    [Theory]
    [InlineData("https://app.mrsoftware.dk", true)]
    [InlineData("https://APP.MRSOFTWARE.DK/", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("https://app.mrsoftware.dk.evil.example", false)]
    public void HasTrustedOrigin_RequiresExactConfiguredOrigin(string origin, bool expected)
    {
        var context = CreateContext(Environments.Production);
        context.Request.Headers.Origin = origin;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://app.mrsoftware.dk"
        }).Build();

        Assert.Equal(expected, RefreshSessionCookie.HasTrustedOrigin(context.Request, configuration));
    }

    private static DefaultHttpContext CreateContext(string environmentName)
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestEnvironment(environmentName))
            .BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services };
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Workslip.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
