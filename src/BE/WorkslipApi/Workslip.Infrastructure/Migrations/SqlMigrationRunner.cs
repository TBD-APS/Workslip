using Dapper;
using Microsoft.AspNetCore.Hosting;

namespace Workslip.Infrastructure.Migrations;

public sealed class SqlMigrationRunner(ISqlConnectionFactory connectionFactory, IWebHostEnvironment environment)
{
    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            if object_id('dbo.SchemaMigrations', 'U') is null
            begin
                create table dbo.SchemaMigrations (
                    Id nvarchar(255) not null constraint PK_SchemaMigrations primary key,
                    AppliedAt datetimeoffset not null constraint DF_SchemaMigrations_AppliedAt default sysutcdatetime()
                );
            end;
            """,
            cancellationToken: cancellationToken));

        var migrationsPath = Path.Combine(environment.ContentRootPath, "Migrations");
        var migrationFiles = Directory.GetFiles(migrationsPath, "*.sql").OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var migrationFile in migrationFiles)
        {
            var migrationId = Path.GetFileName(migrationFile);
            var alreadyApplied = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "select count(1) from dbo.SchemaMigrations where Id = @Id;",
                new { Id = migrationId },
                cancellationToken: cancellationToken));

            if (alreadyApplied > 0)
            {
                continue;
            }

            var sql = await File.ReadAllTextAsync(migrationFile, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "insert into dbo.SchemaMigrations (Id) values (@Id);",
                new { Id = migrationId },
                cancellationToken: cancellationToken));
        }
    }
}
