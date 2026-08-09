using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Api.Configuration;

namespace Workslip.Tests.Configuration;

public sealed class DevConfigurationTests
{
    [Theory]
    [InlineData("Development", false, true)]
    [InlineData("Development", true, true)]
    [InlineData("Staging", true, false)]
    [InlineData("Production", false, false)]
    [InlineData("Production", true, false)]
    public async Task ConfigureDevEnvironment_MapsDevTokenOnlyInDevelopment(
        string environmentName,
        bool releaseTestingEnabled,
        bool expectedDevToken)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName
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
            expectedDevToken,
            routePatterns.Contains("/api/dev/token", StringComparer.Ordinal));
    }
}
