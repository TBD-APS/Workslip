# Browser/mobile PR evidence contract

**Status:** Active  
**Owner:** Workslip maintainers  
**Scope:** Machine-readable handoff contract consumed by `Feature change guard` and exact-head `CI Gate`

This contract operationalizes the Playwright rules in `Docs/agents/VALIDATION.md`. It does not introduce a second browser-test platform: runtime evidence still comes from the existing authenticated ephemeral Playwright runner and focused scenarios.

## When it is required

`tools/release/validate-pr-browser-evidence.mjs` inspects the PR diff. User-visible frontend runtime changes under `src/FE/src/features/`, `src/FE/src/components/` or `src/FE/src/pages/` require an explicit browser-evidence declaration. Tests and generated API/client files do not trigger the requirement by themselves.

The guard infers named critical flows where paths make the risk clear. Current flow names are:

- `auth-session`
- `job-wizard`
- `overview-navigation`
- `worksheet`
- `notifications`
- `customer-lifecycle`
- `people-lifecycle`
- `documents`
- `shared-ui` for visible UI changes that do not map to one of the named flows

A generic green browser suite does not satisfy a named flow automatically. Every inferred flow must be declared and bound to a concrete Playwright script that is actually registered in the exact-head authenticated runner.

## Required evidence form

For a user-visible runtime change, the PR body contains only stable intent fields:

```text
Browser-Evidence: required
Browser-Scenarios: <comma-separated inferred flow names>
Browser-Scripts: <flow=playwright-script.mjs mappings>
Browser-Viewports: <required viewports>
```

Example:

```text
Browser-Evidence: required
Browser-Scenarios: job-wizard, notifications
Browser-Scripts: job-wizard=playwright-critical-job-lifecycle.mjs, notifications=playwright-shared-state-semantics.mjs
Browser-Viewports: desktop-1280, mobile-390
```

`Browser-Scripts` is not a free-form run note. `Feature change guard` verifies that every inferred flow has a mapping, each target has a safe `playwright-*.mjs` basename, and each target is registered through `run_scenario` in the checked-out `src/FE/scripts/run-playwright-ephemeral.sh`. If the focused scenario is not part of that exact-head runner, add/register it before declaring the browser flow covered.

These fields describe what must be proven and should normally remain unchanged while the PR moves from implementation to review. Do not maintain `Browser-Result`, `Browser-Page-Errors` or `Browser-Console-Errors` as merge-gating state in the PR body. Runtime pass/failure belongs to the exact-head CI run so a new commit cannot inherit stale evidence from an older SHA.

For responsive/mobile-sensitive changes, list the narrow viewport that must be exercised (for example `mobile-390`) together with the relevant desktop viewport. The declared script is responsible for exercising those viewports; the guard binds flow to executable runner coverage but does not infer viewport behavior from source text.

## Draft and code-freeze behavior

A draft PR is the implementation lane. Deterministic build/test/API checks may run while code changes, but the expensive authenticated Playwright job is deferred for draft pull requests. Successful draft validation is reported under the separate `Draft CI Gate` check context; it is intentionally not the merge-required `CI Gate` context.

Marking the PR **Ready for review** is the browser-evidence code-freeze point. That transition triggers CI and requires authenticated Playwright against the current PR head. The ready run reports the merge-required `CI Gate` context. A later commit to a ready PR triggers a fresh run for the new head. If implementation needs to resume after browser evidence has started, normally convert the PR back to draft before editing and mark it ready again when the implementation/testability review is complete.

Using distinct draft/ready check contexts prevents a green implementation-lane result on the same SHA from being reused as merge evidence during the Ready transition. This sequencing avoids repeated full browser runs during active implementation while keeping merge evidence bound to the exact code being reviewed.

## Merge-readiness behavior

`Feature change guard` validates the static declaration: UI runtime changes must say `Browser-Evidence: required`, include every inferred flow in `Browser-Scenarios`, map every inferred flow to a registered runner script in `Browser-Scripts`, and declare `Browser-Viewports`.

`CI Gate` owns runtime truth only after code-freeze. For a ready PR with code changes it requires the authenticated Playwright job to succeed on that exact workflow revision. Draft PRs use `Draft CI Gate` and may have Playwright skipped; that check is not merge evidence. Main/release pushes always require the browser job when code changed.

There is no browser waiver or promise-based merge path for user-visible runtime changes. If a required flow has no runnable registered scenario yet, that scenario has to be implemented, registered and exercised before the PR becomes merge-ready.

This contract does not weaken deterministic CI, authorization/tenant evidence, Postman/API evidence, or production release gates.