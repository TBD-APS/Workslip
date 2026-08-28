using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Api.Configuration;
using Workslip.Api.Endpoints;
using Workslip.Application.Users;

namespace Workslip.Tests.Configuration;

public sealed class DemoModeConfigurationTests
{
    [Theory]
    [InlineData("Demo", true, true)]
    [InlineData("Demo", false, false)]
    [InlineData("Development", true, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Production", true, false)]
    public async Task MapDemoEndpoints_MapsTokenOnlyWhenEnvironmentAndFlagMatch(
        string environmentName,
        bool enabled,
        bool expectedDemoToken)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName
        });
        builder.Configuration[DemoModeConfiguration.EnabledKey] = enabled.ToString();
        builder.Services.AddScoped<IUserRepository>(_ =>
            throw new InvalidOperationException("Route registration test must not resolve IUserRepository."));

        await using var app = builder.Build();

        Assert.Equal(expectedDemoToken, DemoModeConfiguration.IsEnabled(app.Environment, app.Configuration));
        app.MapDemoEndpoints();

        var routePatterns = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

        Assert.Equal(
            expectedDemoToken,
            routePatterns.Contains("/api/demo/token", StringComparer.Ordinal));
    }
}
