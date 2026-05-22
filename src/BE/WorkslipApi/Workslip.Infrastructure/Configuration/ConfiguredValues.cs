namespace Workslip.Infrastructure.Configuration;

public static class ConfiguredValues
{
    public static string? FirstConfigured(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
