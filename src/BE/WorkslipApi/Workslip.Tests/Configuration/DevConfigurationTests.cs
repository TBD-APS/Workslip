using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Workslip.Api.Configuration;

namespace Workslip.Tests.Configuration;

public sealed class DevConfigurationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConfigureDevEnvironment_MapsDevTokenOnlyWhenReleaseTestingIsEnabled(
        bool releaseTestingEnabled)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.Services.AddOpenApi();

        await using var app = builder.Build();

        app.ConfigureDevEnvironment(releaseTestingEnabled);

        var routePatterns = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

        Assert.Equal(
            releaseTestingEnabled,
            routePatterns.Contains("/api/dev/token", StringComparer.Ordinal));
    }
}
