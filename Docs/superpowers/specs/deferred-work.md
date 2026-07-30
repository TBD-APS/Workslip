# Deferred Work

## Development and database hardening

- Restrict `ConfigureDevEnvironment` endpoints and developer exception pages to `app.Environment.IsDevelopment()`; the guard is currently commented out.
- Replace or harden the runtime schema-repair SQL in `DatabaseSchemaInitializer`: handle missing `NotificationQueue`, partially created `IdempotencyRecords` indexes/columns, incompatible existing column definitions, and concurrent application startup.
- Diagnose the notification worker's Dapper `QueryAsync` failure using the complete `SqlException.Message` and `SqlException.Number`; the incomplete stack frame does not identify which `NotificationQueue` table or column assumption failed.
- Make development-user reconciliation safe under concurrent API startups if local workflows commonly run more than one instance.

## Existing test-suite failures

- Four `JobCustomerSnapshotTests` use EF relational APIs through the InMemory provider and fail at `EfJobRepository.GetNextReportNumberAsync`.
- `AuditInterceptorTests.Job_repository_update_after_status_activation_logs_real_work_events_without_churn` expects a more detailed status audit message than the current implementation emits.
- Nine `EfInviteRepositoryTests` and `DatabaseIntegrityConstraintTests` fail while creating SQLite fixtures because SQL Server-style `max` column syntax is not accepted by SQLite (`near "max": syntax error`).

## Existing frontend lint failures

- Repository-wide `npm run lint` currently reports 51 errors and 9 warnings in pre-existing files. The failures include React hook/state-effect rules, fast-refresh export rules, render-time ref access, and one `prefer-const` violation; focused lint over the desktop-only Superadmin change passes.
