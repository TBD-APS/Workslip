using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Workslip.Infrastructure.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Schema;

public sealed class WorkslipSchemaRunner(ISqlConnectionFactory connectionFactory, IDatabaseRetryPolicy retryPolicy, IConfiguration configuration)
{
    public Task ApplyAsync(CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("schema.apply", ApplyCoreAsync, cancellationToken);

    private async Task ApplyCoreAsync(CancellationToken cancellationToken)
    {
        await EnsureLocalDatabaseExistsAsync(cancellationToken);

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
            await SeedJobTaxonomyAsync(connection, cancellationToken);

            return;
        }

        if (existingTableNames.Count != expectedTableNames.Length)
        {
            var missingTables = expectedTableNames.Except(existingTableNames, StringComparer.OrdinalIgnoreCase).ToArray();
            await connection.ExecuteAsync(new CommandDefinition(
                WorkslipDatabaseModel.GenerateCreateScript(missingTables),
                cancellationToken: cancellationToken));
        }

        await ApplySchemaUpgradesAsync(connection, cancellationToken);
        await ValidateColumnsAsync(connection, cancellationToken);
        await SeedJobTaxonomyAsync(connection, cancellationToken);
    }

    private async Task EnsureLocalDatabaseExistsAsync(CancellationToken cancellationToken)
    {
        var connectionString = SqlConnectionFactory.ResolveConnectionString(configuration);
        var builder = new SqlConnectionStringBuilder(connectionString);

        if (string.IsNullOrWhiteSpace(builder.InitialCatalog)
            || !builder.DataSource.Contains("(localdb)", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var databaseExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "select case when db_id(@DatabaseName) is null then 0 else 1 end;",
            new { DatabaseName = databaseName },
            cancellationToken: cancellationToken));

        if (databaseExists == 1)
        {
            return;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            $"create database {QuoteSqlIdentifier(databaseName)};",
            cancellationToken: cancellationToken));
    }

    private static string QuoteSqlIdentifier(string value) =>
        $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static async Task ApplySchemaUpgradesAsync(
        System.Data.IDbConnection connection,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            if object_id('dbo.JobReports', 'U') is not null
               and col_length('dbo.JobReports', 'TechnicalObservations') is null
            begin
                alter table dbo.JobReports add TechnicalObservations nvarchar(max) null;
            end;
            """,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            if object_id('dbo.JobControlChecks', 'U') is not null
               and object_id('dbo.FK_JobControlChecks_JobControlSubcategories', 'F') is not null
            begin
                alter table dbo.JobControlChecks drop constraint FK_JobControlChecks_JobControlSubcategories;
            end;
            """,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            if object_id('dbo.JobControlChecks', 'U') is not null
               and col_length('dbo.JobControlChecks', 'CategoryId') is not null
            begin
                declare @constraintName sysname;

                select @constraintName = constraints.name
                from sys.default_constraints constraints
                inner join sys.columns columns on columns.default_object_id = constraints.object_id
                where constraints.parent_object_id = object_id('dbo.JobControlChecks')
                  and columns.name = 'CategoryId';

                if @constraintName is not null
                begin
                    declare @dropDefaultConstraintSql nvarchar(max) = N'alter table dbo.JobControlChecks drop constraint ' + quotename(@constraintName);
                    exec sp_executesql @dropDefaultConstraintSql;
                end;

                alter table dbo.JobControlChecks drop column CategoryId;
            end;
            """,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            if object_id('dbo.JobControlSubcategories', 'U') is not null
            begin
                drop table dbo.JobControlSubcategories;
            end;
            """,
            cancellationToken: cancellationToken));
    }

    private async Task SeedJobTaxonomyAsync(
        System.Data.IDbConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var workKind in configuration.GetSection("JobTaxonomy:WorkKinds").GetChildren())
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                merge dbo.JobWorkKinds as target
                using (select @Id as Id) as source on target.Id = source.Id
                when matched then update set Label = @Label, RequiresCustomWorkKind = @RequiresCustomWorkKind, IsActive = @IsActive, SortOrder = @SortOrder, UpdatedAt = sysutcdatetime()
                when not matched then insert (Id, Label, RequiresCustomWorkKind, IsActive, SortOrder, UpdatedAt) values (@Id, @Label, @RequiresCustomWorkKind, @IsActive, @SortOrder, sysutcdatetime());
                """,
                new
                {
                    Id = workKind["Id"],
                    Label = workKind["Label"],
                    RequiresCustomWorkKind = bool.Parse(workKind["RequiresCustomWorkKind"] ?? "false"),
                    IsActive = bool.Parse(workKind["IsActive"] ?? "true"),
                    SortOrder = int.Parse(workKind["SortOrder"] ?? "0")
                },
                cancellationToken: cancellationToken));
        }

        foreach (var closureFlag in configuration.GetSection("JobTaxonomy:ClosureFlags").GetChildren())
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                merge dbo.JobClosureFlags as target
                using (select @Id as Id) as source on target.Id = source.Id
                when matched then update set Label = @Label, IsExclusive = @IsExclusive, IsActive = @IsActive, SortOrder = @SortOrder, UpdatedAt = sysutcdatetime()
                when not matched then insert (Id, Label, IsExclusive, IsActive, SortOrder, UpdatedAt) values (@Id, @Label, @IsExclusive, @IsActive, @SortOrder, sysutcdatetime());
                """,
                new
                {
                    Id = closureFlag["Id"],
                    Label = closureFlag["Label"],
                    IsExclusive = bool.Parse(closureFlag["IsExclusive"] ?? "false"),
                    IsActive = bool.Parse(closureFlag["IsActive"] ?? "true"),
                    SortOrder = int.Parse(closureFlag["SortOrder"] ?? "0")
                },
                cancellationToken: cancellationToken));
        }
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
