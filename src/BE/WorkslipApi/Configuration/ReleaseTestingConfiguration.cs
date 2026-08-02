namespace Workslip.Api.Configuration;

public static class ReleaseTestingConfiguration
{
    public const string EnabledKey = "ReleaseTesting:Enabled";

    public static bool IsEnabled(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        if (environment.IsDevelopment())
            return true;

        var configuredValue = configuration[EnabledKey];
        return bool.TryParse(configuredValue, out var enabled) && enabled;
    }
}
