# Job API migrations

SQL migrations live in this folder and are applied in filename order by `SqlMigrationRunner`.

For the first deployment, run the API with a `ConnectionStrings:JobDB` value pointing to Azure SQL and execute `SqlMigrationRunner.ApplyAsync` from an admin-only startup task or one-off deployment command.

The API does not automatically run migrations on every startup yet. That is intentional for the first draft so schema changes remain explicit.
