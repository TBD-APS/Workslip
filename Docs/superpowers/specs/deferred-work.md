# Deferred Work

## Development and database hardening

- Restrict `ConfigureDevEnvironment` endpoints and developer exception pages to `app.Environment.IsDevelopment()`; the guard is currently commented out.
- Replace or harden the runtime schema-repair SQL in `DatabaseSchemaInitializer`: handle missing `NotificationQueue`, partially created `IdempotencyRecords` indexes/columns, incompatible existing column definitions, and concurrent application startup.
- Diagnose the notification worker's Dapper `QueryAsync` failure using the complete `SqlException.Message` and `SqlException.Number`; the incomplete stack frame does not identify which `NotificationQueue` table or column assumption failed.
- Make development-user reconciliation safe under concurrent API startups if local workflows commonly run more than one instance.

## Existing test-suite failures

- Four `JobCustomerSnapshotTests` use EF relational APIs through the InMemory provider and fail at `EfJobRepository.GetNextReportNumberAsync`.
- `AuditInterceptorTests.Job_repository_update_after_status_activation_logs_real_work_events_without_churn` expects a more detailed status audit message than the current implementation emits.
