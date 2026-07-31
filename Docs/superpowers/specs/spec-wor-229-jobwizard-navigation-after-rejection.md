---
title: 'WOR-229: Preserve JobWizard navigation after rejection'
type: 'bugfix'
created: '2026-07-31'
status: 'done'
baseline_commit: '0912bede33475ed616c31836f07361cf0dedf1ed'
context:
  - 'Docs/agents/OPERATING_CONTRACT.md'
  - 'Docs/agents/VALIDATION.md'
  - 'src/FE/AGENTS.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** When an ordinary user opens a rejected case, `JobDetail` correctly lands on the first JobWizard step, but the same effect runs after every step change and immediately forces the wizard back to step 1. The user therefore cannot inspect or correct the rejected case before resubmitting it.

**Approach:** Treat the first-step reset as one-time landing normalization for each rejected job route, and prevent the existing assigned-user worksheet shortcut from overriding that rejected landing when data resolves later. Preserve subsequent user navigation and all existing validation, saving, seen-state, rejection-banner, and role-specific routing behavior.

## Boundaries & Constraints

**Always:** Keep rejected jobs editable for ordinary users; open each newly loaded rejected job on step 1 regardless of job/reference-data response order; allow navigation after that landing action; retain Admin/Superadmin redirection to the completed-report route; preserve current forward-step validation and persistence behavior; add focused regression coverage.

**Ask First:** Any change to which roles may edit or undo rejection, job-status transition rules, validation requirements, backend/API behavior, or permanent test dependencies.

**Never:** Disable wizard validation to make navigation pass; change generated API files; alter rejection notes, notifications, seen-state semantics, or the admin undo-rejection flow; include unrelated cleanup.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|----------------------------|----------------|
| Rejected landing | Ordinary user opens a rejected job while the route hook holds another step | Wizard opens on step 1 and scrolls to the top once | Existing loading/error UI remains unchanged |
| Navigation after landing | Landing normalization has run and required prior steps are valid | Step indicator, Next, and Back navigation retain the selected step | Existing validation message blocks only invalid forward navigation |
| Different rejected job | The mounted route changes to another rejected job | The new job independently opens on step 1 once | No state leaks from the prior job |
| Delayed reference data | Rejected assigned job and worksheet load before reference data | The later worksheet shortcut does not move the user away from step 1 | Existing shortcut remains active for eligible non-rejected jobs |
| Admin rejected job | Admin or Superadmin opens the rejected editable route | Existing redirect to `/app/completed/{id}` remains intact | No duplicate seen-state request is introduced |
| Non-rejected job | User opens or navigates through Draft/InReview/Approved state | Existing route and wizard behavior are unchanged | Existing status-specific handling remains authoritative |

</frozen-after-approval>

## Code Map

- `src/FE/src/features/jobs/routes/JobDetail.tsx` -- owns rejected-job landing normalization, seen-state updates, and the admin redirect.
- `src/FE/src/features/jobs/routes/JobDetail.test.tsx` -- focused route regression coverage for one-time rejected landing behavior and role boundaries.
- `src/FE/src/features/jobs/components/JobDetails.tsx` -- owns step controls and forward-step validation; inspect to confirm behavior but change only if evidence requires it.
- `src/FE/src/features/jobs/hooks/useJobDetails.ts` -- owns current-step state, save-before-navigation, validation, and the assigned-user worksheet shortcut whose delayed execution can override rejected landing.

## Tasks & Acceptance

**Execution:**
- [x] `src/FE/src/features/jobs/routes/JobDetail.tsx` -- scope rejected landing normalization to one execution per loaded rejected job while retaining explicit dependencies and existing admin routing.
- [x] `src/FE/src/features/jobs/hooks/useJobDetails.ts` -- exclude rejected jobs from the automatic worksheet-step shortcut so asynchronous reference data cannot race the rejected landing behavior.
- [x] `src/FE/src/features/jobs/routes/JobDetail.test.tsx` and focused hook coverage -- prove initial normalization, continued navigation, reset for a different job, delayed reference-data behavior, admin behavior, and non-rejected behavior with deterministic mocks.
- [x] `Docs/superpowers/specs/spec-wor-229-jobwizard-navigation-after-rejection.md` -- record implementation/review evidence and keep the final PR documentation waiver explicit because no maintained job-transition document currently exists.

**Acceptance Criteria:**
- Given an ordinary user has opened a rejected case, when they navigate through valid JobWizard steps using step indicators, Next, or Back, then the chosen step remains visible and the route does not force them back to step 1.
- Given a different rejected case loads in the same route instance, when its data resolves, then that case opens on step 1 exactly once.
- Given a rejected case qualifies for the assigned-user worksheet shortcut, when reference data resolves after the job, then the case remains on step 1 until the user navigates.
- Given an Admin or Superadmin opens a rejected case, when routing resolves, then they still reach the completed report and the existing undo-rejection flow remains available.
- Given the change is reviewed, when the PR is prepared, then it contains no backend, generated-client, dependency, or unrelated worktree changes.

## Implementation Evidence

- Implemented a per-loaded-job rejected landing guard and excluded rejected jobs from the asynchronous worksheet shortcut.
- Added seven focused Vitest scenarios across the route and hook boundaries; all seven pass, including actual route-ID changes, one-time scrolling, seen-state preservation, status transitions, and both job/reference-data response orders.
- Affected-file ESLint completes with zero errors and one pre-existing `react-hooks/exhaustive-deps` warning at `useJobDetails.ts:308`.
- `npm run build` remains pending after local API-client regeneration: the ignored local `UserViewModel` artifact lacks the already-referenced `roleDisplayName` field in three existing tests.
- Three independent reviews found no frozen-intent or architecture gap. Their route-transition edge case was patched by requiring route and loaded-job identity to match before normalization.
- Playwright remains pending in the publishing workflow. The final PR must include a documentation waiver owned by the Workslip frontend owner, expiring 2026-08-14, because no maintained job-transition document currently covers this route behavior.

## Spec Change Log

## Design Notes

Key the landing guard to the loaded job rather than to component lifetime alone. A job identity change must re-arm the landing action, while ordinary `currentStep` changes for the same rejected job must not. The worksheet shortcut should retain its current behavior for eligible non-rejected jobs only.

## Verification

**Commands:**
- `npx eslint src/features/jobs/routes/JobDetail.tsx src/features/jobs/routes/JobDetail.test.tsx src/features/jobs/hooks/useJobDetails.ts src/features/jobs/hooks/useJobDetails.test.tsx` -- expected: affected frontend files have no lint errors.
- `npx vitest run src/features/jobs/routes/JobDetail.test.tsx src/features/jobs/hooks/useJobDetails.test.tsx` -- expected: rejected landing, navigation, and response-order regression scenarios pass.
- `npm run build` -- expected: TypeScript, service-worker typecheck, and the production Vite build pass.
- `npx playwright test --config .tmp-wor-229/playwright.config.ts` -- expected: deterministic rejected-user navigation and admin redirect scenarios pass in desktop Chromium and a narrow mobile viewport with no console, page, or failed-request errors.

## Suggested Review Order

**Rejected-job landing**

- Match route and loaded identity, then normalize each rejected landing exactly once.
  [`JobDetail.tsx:36`](../../../src/FE/src/features/jobs/routes/JobDetail.tsx#L36)

- Keep delayed worksheet auto-navigation from overriding rejected-job correction flow.
  [`useJobDetails.ts:141`](../../../src/FE/src/features/jobs/hooks/useJobDetails.ts#L141)

**Regression boundaries**

- Exercise route changes, continued navigation, scrolling, seen-state, status changes, and admin redirect.
  [`JobDetail.test.tsx:82`](../../../src/FE/src/features/jobs/routes/JobDetail.test.tsx#L82)

- Cover both job-first and reference-data-first asynchronous resolution orders.
  [`useJobDetails.test.tsx:186`](../../../src/FE/src/features/jobs/hooks/useJobDetails.test.tsx#L186)
