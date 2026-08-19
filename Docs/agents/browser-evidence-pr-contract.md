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

A generic browser scenario does not satisfy a named flow declaration automatically. The PR declares which changed flows and viewports must be covered; the exact-head CI run is the source of truth for whether runtime browser evidence passed.

## Required evidence form

For a user-visible runtime change, the PR body contains only stable intent fields:

```text
Browser-Evidence: required
Browser-Scenarios: <comma-separated inferred flow names>
Browser-Viewports: <required viewports>
```

These fields describe what must be proven and should normally remain unchanged while the PR moves from implementation to review. Do not maintain `Browser-Result`, `Browser-Page-Errors` or `Browser-Console-Errors` as merge-gating state in the PR body. Runtime pass/failure belongs to the exact-head CI run so a new commit cannot inherit stale evidence from an older SHA.

For responsive/mobile-sensitive changes, list the narrow viewport that must be exercised (for example `mobile-390`) together with the relevant desktop viewport. The evidence should also cover keyboard/focus/safe-area behavior when that is part of the changed risk.

## Draft and code-freeze behavior

A draft PR is the implementation lane. Deterministic build/test/API checks may run while code changes, but the expensive authenticated Playwright job is deferred for draft pull requests.

Marking the PR **Ready for review** is the browser-evidence code-freeze point. That transition triggers CI and requires authenticated Playwright against the current PR head. A later commit to a ready PR triggers a fresh run for the new head. If implementation needs to resume after browser evidence has started, normally convert the PR back to draft before editing and mark it ready again when the implementation/testability review is complete.

This sequencing prevents repeated full browser runs during active implementation while keeping merge evidence bound to the exact code being reviewed.

## Merge-readiness behavior

`Feature change guard` validates the static declaration: UI runtime changes must say `Browser-Evidence: required`, include every inferred flow in `Browser-Scenarios`, and declare `Browser-Viewports`.

`CI Gate` owns runtime truth. For a ready PR with code changes it requires the authenticated Playwright job to succeed on that exact workflow revision. Draft PRs may have Playwright skipped because GitHub draft state already prevents merge readiness. Main/release pushes always require the browser job when code changed.

There is no browser waiver or promise-based merge path for user-visible runtime changes. If a required flow has no runnable scenario yet, the flow has to be implemented and exercised before the PR becomes merge-ready.

This contract does not weaken deterministic CI, authorization/tenant evidence, Postman/API evidence, or production release gates.