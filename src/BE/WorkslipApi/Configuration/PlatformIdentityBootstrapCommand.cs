using Microsoft.EntityFrameworkCore;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Configuration;

public static class PlatformIdentityBootstrapCommand
{
    public const string ConfigurationKey = "Workslip:Operation";
    public const string OperationName = "bootstrap-superadmins";

    public static bool IsRequested(IReadOnlyList<string> args)
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
                {
                    throw new InvalidOperationException($"Missing value for '{optionName}'.");
                }

                candidate = args[index];
            }

            if (candidate is null)
            {
                continue;
            }

            if (operation is not null)
            {
                throw new InvalidOperationException($"'{optionName}' can only be supplied once.");
            }

            operation = candidate.Trim();
        }

        if (operation is null)
            return false;

        if (string.IsNullOrWhiteSpace(operation))
        {
            throw new InvalidOperationException($"'{optionName}' requires a non-empty value.");
        }

        if (string.Equals(operation, OperationName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new InvalidOperationException(
            $"Unsupported Workslip operation '{operation}'. Supported operation: '{OperationName}'.");
    }

    public static async Task ExecuteAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        if (!await db.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException("Database connectivity check returned false.");
        }

        await scope.ServiceProvider
            .GetRequiredService<PlatformIdentityBootstrapper>()
            .BootstrapAsync(cancellationToken);
    }
}
