using Dapper;
using Workslip.Infrastructure.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Schema;

public sealed class WorkslipSchemaRunner(ISqlConnectionFactory connectionFactory, IDatabaseRetryPolicy retryPolicy)
{
    public Task ApplyAsync(CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("schema.apply", ApplyCoreAsync, cancellationToken);

    private async Task ApplyCoreAsync(CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var expectedTableNames = WorkslipDatabaseModel.Tables.Select(table => table.Name).ToArray();
        var existingTableNames = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            select t.name
            from sys.tables t
            inner join sys.schemas s on s.schema_id = t.schema_id
            where s.name = @Schema
              and t.name in @TableNames;
            """,
            new { WorkslipDatabaseModel.Schema, TableNames = expectedTableNames },
            cancellationToken: cancellationToken))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (existingTableNames.Count == 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                WorkslipDatabaseModel.GenerateCreateScript(),
                cancellationToken: cancellationToken));

            return;
        }

        if (existingTableNames.Count != expectedTableNames.Length)
        {
            var missingTables = expectedTableNames.Except(existingTableNames, StringComparer.OrdinalIgnoreCase);
            throw new InvalidOperationException(
                "Database schema is partial. Missing model tables: " + string.Join(", ", missingTables));
        }

        await ValidateColumnsAsync(connection, cancellationToken);
    }

    private static async Task ValidateColumnsAsync(
        System.Data.IDbConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var table in WorkslipDatabaseModel.Tables)
        {
            var expectedColumnNames = table.Columns.Select(column => column.Name).ToArray();
            var existingColumnNames = (await connection.QueryAsync<string>(new CommandDefinition(
                """
                select c.name
                from sys.columns c
                where c.object_id = object_id(@QualifiedTableName)
                  and c.name in @ColumnNames;
                """,
                new { QualifiedTableName = table.QualifiedName, ColumnNames = expectedColumnNames },
                cancellationToken: cancellationToken))).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (existingColumnNames.Count == expectedColumnNames.Length)
            {
                continue;
            }

            var missingColumns = expectedColumnNames.Except(existingColumnNames, StringComparer.OrdinalIgnoreCase);
            throw new InvalidOperationException(
                $"Database schema is out of date. Missing model columns on {table.QualifiedName}: " + string.Join(", ", missingColumns));
        }
    }
}
