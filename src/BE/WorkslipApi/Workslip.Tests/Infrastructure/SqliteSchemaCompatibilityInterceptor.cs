using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Workslip.Tests.Infrastructure;

internal sealed class SqliteSchemaCompatibilityInterceptor : DbCommandInterceptor
{
    internal static readonly SqliteSchemaCompatibilityInterceptor Instance = new();

    private SqliteSchemaCompatibilityInterceptor()
    {
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        RewriteSchemaTypes(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        RewriteSchemaTypes(command);
        return ValueTask.FromResult(result);
    }

    private static void RewriteSchemaTypes(DbCommand command)
    {
        command.CommandText = command.CommandText.Replace(
            "nvarchar(max)",
            "TEXT",
            StringComparison.OrdinalIgnoreCase);
    }
}
