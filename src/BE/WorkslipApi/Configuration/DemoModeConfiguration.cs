namespace Workslip.Api.Configuration;

public static class DemoModeConfiguration
{
    public const string EnabledKey = "DemoMode:Enabled";
    public const string DemoEnvironmentName = "Demo";

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsEnvironment(DemoEnvironmentName)
        && configuration.GetValue<bool>(EnabledKey);
}
