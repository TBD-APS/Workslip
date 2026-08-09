namespace Workslip.Api.Configuration;

internal static class WorkslipOperationParser
{
    public const string ConfigurationKey = "Workslip:Operation";

    public static string? Parse(IReadOnlyList<string> args)
    {
        string? operation = null;
        var optionName = $"--{ConfigurationKey}";
        var inlinePrefix = $"{optionName}=";

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            string? candidate = null;
            if (argument.StartsWith(inlinePrefix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = argument[inlinePrefix.Length..];
            }
            else if (string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Count)
                    throw new InvalidOperationException($"Missing value for '{optionName}'.");

                candidate = args[index];
            }

            if (candidate is null)
                continue;

            if (operation is not null)
                throw new InvalidOperationException($"'{optionName}' can only be supplied once.");

            operation = candidate.Trim();
        }

        if (operation is null)
            return null;

        if (string.IsNullOrWhiteSpace(operation))
            throw new InvalidOperationException($"'{optionName}' requires a non-empty value.");

        return operation;
    }
}
