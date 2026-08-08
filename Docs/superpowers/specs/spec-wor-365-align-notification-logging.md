---
title: 'WOR-365: Align notification logging with correct log levels'
type: 'chore'
created: '2026-08-08'
status: 'done'
baseline_commit: 'bcab2e10ee198abbbf2e4b5300ac265eb0782cf3'
context:
  - '{project-root}/Docs/agents/VALIDATION.md'
  - '{project-root}/Docs/compliance/GDPR_AI_ACT_BASELINE.md'
  - '{project-root}/Docs/architecture/adr/0003-vapid-key-rotation-and-subscription-repair.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Push notification telemetry currently reports routine worker, provider, and processor activity as errors, duplicates failed-attempt signals, and includes unnecessary user identifiers in adjacent queue logs. This creates Application Insights noise and weakens the privacy boundary around notification data.

**Approach:** Assign each notification outcome one deliberate operational owner and severity: diagnostic success details at Debug, worker lifecycle at Information, recoverable/expected abnormal outcomes at Warning, and terminal or infrastructure failures at Error. Keep messages structured, concise, and limited to safe identifiers and bounded counts.

## Boundaries & Constraints

**Always:** Preserve notification status, retry timing/counts, subscription deactivation, delivery persistence, cancellation, and batch isolation; emit at most one processor outcome log for a failed delivery attempt; keep real terminal/infrastructure failures discoverable; treat stale subscriptions as an expected repair path; verify rendered messages, structured properties, and exception channels for sensitive data.

**Ask First:** Changing notification behavior or API contracts; changing generic database retry logging; adding a provider abstraction solely for tests; expanding into frontend/service-worker telemetry or broader job logging.

**Never:** Log notification payloads, localized title/body, names, addresses, report numbers, e-mails, user IDs, device endpoints, subscription keys/tokens, VAPID material, provider response bodies, or raw exception details that may contain them; retain temporary `PUSH TRACE` error logging; weaken failure handling to reduce telemetry.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|---------------------------|----------------|
| Healthy delivery | Active subscription and provider success | Processing/delivery details are Debug; worker start/stop remain Information | No Warning or Error |
| No destination | User has no active subscriptions | Notification completes without sending | Diagnostic Debug only |
| Expired subscription | Provider reports 404/410 | Subscription is disabled and notification completes | One sanitized Warning lifecycle outcome; no Error |
| Retryable failure | Provider failure before retry limit | Notification is rescheduled unchanged | One sanitized Warning for the attempt |
| Terminal failure | Invalid payload/type, exhausted retry, or escaped infrastructure failure | Existing failed/rescheduled state is preserved | One sanitized Error owned by the handling boundary |

</frozen-after-approval>

## Code Map

- `src/BE/WorkslipApi/Workslip.Infrastructure/Notifications/PushNotificationProcessor.cs` -- owns per-notification processing, retry lifecycle, aggregate delivery outcomes, and terminal poison-item handling.
- `src/BE/WorkslipApi/Workslip.Infrastructure/Notifications/PushNotificationWorker.cs` -- owns hosted-worker lifecycle, batch diagnostics, and escaped cycle failures.
- `src/BE/WorkslipApi/Workslip.Infrastructure/Notifications/WebPushSender.cs` -- owns the Web Push boundary and currently contains temporary error-level traces.
- `src/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs` -- contains duplicated, inaccurate review-notification queue logs with display names.
- `src/BE/WorkslipApi/Workslip.Application/Notifications/JobDeletionNotificationService.cs` -- owns suppressed per-recipient queue failures and currently logs a user identifier.
- `src/BE/WorkslipApi/Workslip.Tests/Notifications/PushNotificationProcessorTests.cs` and `PushNotificationMonitoringTests.cs` -- cover delivery state and sanitized telemetry, but currently lock incorrect severity and do not capture the full structured log contract.

## Tasks & Acceptance

**Execution:**
- [x] `PushNotificationProcessor.cs` -- normalize outcome severity, add consistent terminal poison-item telemetry, and consolidate retry/terminal logging so one failed attempt cannot produce duplicate outcomes.
- [x] `PushNotificationWorker.cs` and `WebPushSender.cs` -- remove temporary error traces, retain meaningful lifecycle/failure signals, and prevent provider exception details from crossing the safe logging boundary.
- [x] `JobService.cs` and `JobDeletionNotificationService.cs` -- remove personal identifiers and replace duplicate/inaccurate send traces with a single truthful diagnostic event where useful.
- [x] Notification tests -- capture level, template/properties, rendered message, and exception; cover success, no-subscription, expired, retryable, exhausted, invalid payload/type, and unexpected-failure severity/redaction.

**Acceptance Criteria:**
- Given healthy push processing, when delivery completes, then routine logs are Debug/Information and no Warning/Error is emitted.
- Given a recoverable or expired delivery, when it is retried or repaired, then exactly one sanitized Warning describes the outcome.
- Given a terminal or infrastructure failure, when the operation cannot complete as intended, then exactly one owning Error remains easy to query.
- Given any notification/subscription path, when logs are captured, then no prohibited personal, payload, endpoint, key, token, provider-body, or unsafe exception data appears.
- Given the change, when behavioral tests run, then queue state, retries, delivery records, subscription lifecycle, cancellation, and API behavior are unchanged.

## Spec Change Log

## Design Notes

The processor owns notification-level outcomes, the worker owns only failures escaping a cycle, and the provider boundary may retain safe failure classification without re-logging the processor outcome. This gives Application Insights one actionable event per operational failure while preserving low-level diagnosis at Debug.

## Verification

**Commands:**
- `dotnet restore src/BE/WorkslipApi/Workslip.slnx` -- expected: dependencies restore successfully.
- `dotnet test src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj -c Release --filter "FullyQualifiedName~Workslip.Tests.Notifications.PushNotificationProcessorTests|FullyQualifiedName~Workslip.Tests.Notifications.PushNotificationMonitoringTests"` -- expected: focused notification behavior and logging tests pass.
- `dotnet build src/BE/WorkslipApi/Workslip.slnx -c Release --no-restore` -- expected: backend compiles without errors.
- `rg -n "PUSH TRACE|Log(Error|Warning|Information|Debug)" src/BE/WorkslipApi/Workslip.Infrastructure/Notifications src/BE/WorkslipApi/Workslip.Application/Notifications` -- expected: no temporary trace remains and each call matches the approved severity matrix.

**Manual checks (if no CLI):**
- Inspect the final diff for logging-only behavior, single-owner failure events, bounded structured fields, and absence of personal/subscription material; external Web Push delivery and deployed Application Insights payload inspection remain environment-dependent smoke checks.

**Recorded results (2026-08-08):**
- Dependency restore completed during implementation.
- Focused Release tests passed 25/25 after adversarial-review fixes, including persistence-failure outcome ownership and structured redaction contracts.
- Release solution build succeeded with 0 warnings and 0 errors.
- `git diff --check` passed; static review found no remaining `PUSH TRACE`, payload, user, endpoint, key, token, or raw-exception logging in the changed notification paths.
- No HTTP contract, relational behavior, frontend, Playwright, deployed Web Push, or Application Insights smoke was required/executed because behavior and API contracts are unchanged; deployed telemetry inspection remains environment-dependent.

## Suggested Review Order

**Outcome ownership and severity**

- Start with the processor lifecycle and its sanitized terminal branches.
  [`PushNotificationProcessor.cs:54`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Notifications/PushNotificationProcessor.cs#L54)

- State transitions now succeed before delivery outcomes are emitted.
  [`PushNotificationProcessor.cs:194`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Notifications/PushNotificationProcessor.cs#L194)

- Retry and terminal failures produce exactly one persisted outcome.
  [`PushNotificationProcessor.cs:267`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Notifications/PushNotificationProcessor.cs#L267)

**Boundary logging and privacy**

- Worker lifecycle is Information, claimed batches Debug, escaped failures sanitized Error.
  [`PushNotificationWorker.cs:25`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Notifications/PushNotificationWorker.cs#L25)

- Provider traces and raw exception responses are removed at the Web Push boundary.
  [`WebPushSender.cs:7`](../../../src/BE/WorkslipApi/Workslip.Infrastructure/Notifications/WebPushSender.cs#L7)

- Review notification queuing uses one aggregate Debug without recipient identity.
  [`JobService.cs:400`](../../../src/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs#L400)

- Suppressed deletion-queue failures retain safe type diagnostics without user data.
  [`JobDeletionNotificationService.cs:45`](../../../src/BE/WorkslipApi/Workslip.Application/Notifications/JobDeletionNotificationService.cs#L45)

**Regression protection and follow-up**

- Severity, exact templates, properties, and redaction are asserted per outcome.
  [`PushNotificationMonitoringTests.cs:52`](../../../src/BE/WorkslipApi/Workslip.Tests/Notifications/PushNotificationMonitoringTests.cs#L52)

- Persistence-failure tests guard against duplicate or misleading processor outcomes.
  [`PushNotificationMonitoringTests.cs:275`](../../../src/BE/WorkslipApi/Workslip.Tests/Notifications/PushNotificationMonitoringTests.cs#L275)

- Pre-existing provider cancellation behavior is deferred outside this logging-only issue.
  [`deferred-work.md:25`](deferred-work.md#L25)
