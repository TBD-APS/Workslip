using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application;
using Workslip.Infrastructure;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class AcsEmailServiceTests
{
    [Fact]
    public async Task SendOtcEmailAsync_AcsNotConfiguredInDevelopment_SkipsWithoutThrowing()
    {
        var service = CreateService("Development");

        await service.SendOtcEmailAsync("user@example.test", "123456", CancellationToken.None);
        await service.SendInviteEmailAsync("user@example.test", "token", CancellationToken.None);
    }

    [Fact]
    public async Task SendOtcEmailAsync_AcsNotConfiguredOutsideDevelopment_Throws()
    {
        var service = CreateService("Production");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendOtcEmailAsync("user@example.test", "123456", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendInviteEmailAsync("user@example.test", "token", CancellationToken.None));
    }

    private static AcsEmailService CreateService(string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = environment
            })
            .Build();

        return new AcsEmailService(
            configuration,
            NullLogger<AcsEmailService>.Instance,
            new FakeCorrelationIdAccessor());
    }

    private sealed class FakeCorrelationIdAccessor : ICorrelationIdAccessor
    {
        public string CorrelationId => "test-correlation";
    }
}
