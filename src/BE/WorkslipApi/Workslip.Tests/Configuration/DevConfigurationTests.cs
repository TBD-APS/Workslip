using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Workslip.Api.Configuration;

namespace Workslip.Tests.Configuration;

public sealed class DevConfigurationTests
{
    [Theory]
    [InlineData(Environments.Development, false, true)]
    [InlineData(Environments.Development, true, true)]
    [InlineData(Environments.Staging, true, false)]
    [InlineData(Environments.Production, false, false)]
    [InlineData(Environments.Production, true, false)]
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
