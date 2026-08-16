using Workslip.Application.Operations;

namespace Workslip.Api.Services;

public sealed class WorkslipApplicationEnvironmentRegistry(IHostEnvironment environment)
    : IApplicationEnvironmentRegistry
{
    public Task<IReadOnlyList<ApplicationEnvironmentRegistration>> ListAsync(
        CancellationToken cancellationToken)
    {
        var environmentName = environment.EnvironmentName.Trim().ToLowerInvariant();
        var registration = new ApplicationEnvironmentRegistration(
            new ApplicationEnvironmentKey("workslip", environmentName),
            "Workslip",
            [
                new ControlCenterSourceRegistration(
                    ControlCenterSignalKind.Health,
                    new EvidenceReference("workslip-api", "/health")),
                new ControlCenterSourceRegistration(
                    ControlCenterSignalKind.Automation,
                    new EvidenceReference("github-actions", "rasm105k/Workslip-v2.0"))
            ]);

        return Task.FromResult<IReadOnlyList<ApplicationEnvironmentRegistration>>([registration]);
    }
}
