# Browser/mobile PR evidence contract

**Status:** Active  
**Owner:** Workslip maintainers  
**Scope:** Machine-readable handoff contract consumed by `Feature change guard`

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

A generic green browser job does not satisfy a named flow declaration automatically.

## Required evidence form

Before merge-readiness, the PR body must contain:

```text
Browser-Evidence: required
Browser-Scenarios: <comma-separated inferred flow names>
Browser-Result: passed
Browser-Viewports: <actual tested viewports>
Browser-Page-Errors: 0
Browser-Console-Errors: 0
```

Keep run links and human-readable evidence notes next to that block in the PR body. `pending`, missing fields, missing inferred scenarios, page errors, or console errors keep the guard red. So does `Browser-Evidence: waived`, which is no longer accepted.

For responsive/mobile-sensitive changes, list the narrow viewport actually exercised (for example `mobile-390`) together with the relevant desktop viewport. The evidence should also cover keyboard/focus/safe-area behavior when that is part of the changed risk.

## Merge-readiness behavior

The Feature Change Guard reruns when the PR body is edited. This lets implementation begin with `pending` evidence while keeping the PR fail-closed until the relevant browser scenario has actually run.

There is no waiver or exemption path. A UI runtime change cannot reach merge-readiness on a promise, an owner's sign-off or a scoped-risk argument — only on a passing scenario for every inferred flow. If a required flow has no runnable scenario yet, the flow has to be written and run before the change can merge.

This guard does not weaken deterministic CI, the existing authenticated Playwright job, authorization/tenant evidence, or production release gates.
