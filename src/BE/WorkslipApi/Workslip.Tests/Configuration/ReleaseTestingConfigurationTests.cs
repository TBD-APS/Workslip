using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Workslip.Api.Configuration;

namespace Workslip.Tests.Configuration;

public sealed class ReleaseTestingConfigurationTests
{
    [Fact]
    public void IsEnabled_AllowsLocalDevelopmentWithoutExplicitConfiguration()
    {
        var result = ReleaseTestingConfiguration.IsEnabled(
            new TestHostEnvironment(Environments.Development),
            BuildConfiguration(null));

        Assert.True(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("invalid")]
    public void IsEnabled_FailsClosedOutsideDevelopment(string? configuredValue)
    {
        var result = ReleaseTestingConfiguration.IsEnabled(
            new TestHostEnvironment(Environments.Production),
            BuildConfiguration(configuredValue));

        Assert.False(result);
    }

    [Fact]
    public void IsEnabled_AllowsExplicitPreLiveReleaseTestingOutsideDevelopment()
    {
        var result = ReleaseTestingConfiguration.IsEnabled(
            new TestHostEnvironment(Environments.Production),
            BuildConfiguration("true"));

        Assert.True(result);
    }

    private static IConfiguration BuildConfiguration(string? configuredValue)
    {
        var values = configuredValue is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>
            {
                [ReleaseTestingConfiguration.EnabledKey] = configuredValue
            };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Workslip.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
