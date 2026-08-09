using Microsoft.EntityFrameworkCore;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Configuration;

public static class PlatformIdentityBootstrapCommand
{
    public const string ConfigurationKey = WorkslipOperationParser.ConfigurationKey;
    public const string OperationName = "bootstrap-superadmins";

    public static bool IsRequested(IReadOnlyList<string> args)
    {
        var operation = WorkslipOperationParser.Parse(args);
        if (operation is null)
            return false;

        if (string.Equals(operation, OperationName, StringComparison.OrdinalIgnoreCase))
            return true;

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
