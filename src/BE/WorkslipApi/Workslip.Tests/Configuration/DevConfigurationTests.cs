using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Workslip.Api.Configuration;

namespace Workslip.Tests.Configuration;

public sealed class DevConfigurationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConfigureDevEnvironment_NeverMapsDevToken(
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

        Assert.DoesNotContain("/api/dev/token", routePatterns, StringComparer.Ordinal);
    }
}
