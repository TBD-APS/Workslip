# Workslip frontend instructions

Root [`../../AGENTS.md`](../../AGENTS.md) applies. These are frontend-specific rules for `src/FE/`.

## Architecture and state

- Reuse established feature folders, shared UI/form components, query helpers and generated/shared API clients.
- Do not bypass the established API client with ad hoc `fetch` or another Axios instance.
- Keep server state in React Query; use local state for UI state or explicit edit drafts, not duplicated server truth.
- Avoid unnecessary effects, derived-state synchronization and provider expansion.
- Split components only when the split creates a real responsibility boundary.
- Keep authorization decisions on the backend. Frontend guards are navigation/presentation only.
- Include user/tenant/session context in cache keys when isolation requires it, and clear relevant state when that context changes.

## Shared form and UI conventions

Use `src/components/forms/` and existing shared controls before creating new ones.

Use `NumericInput` instead of raw `<input type="number">` where numeric entry is required; native number inputs can lose Danish decimal-comma input.

### Shared presentation and localization

- Cross-feature presentation conventions belong in `src/lib/presentation/`. Locale selection, dates/times and future standardized number/currency/text presentation must have one shared owner instead of feature-local formatting rules.
- `src/lib/presentation/locale.ts` owns the product UI locale. Do not hard-code locale identifiers in feature code.
- All user-visible date/time formatting must go through the shared date functions. Feature/components must not introduce their own `Intl.DateTimeFormat`, `toLocaleDateString`, `toLocaleTimeString`, `toLocaleString` or hand-built presentation date strings.
- The canonical Workslip date-only presentation is Danish abbreviated month text: `17. aug. 2026`. Use `formatDate()` for date-only values.
- Do not use numeric-only date presentation such as `17.08.2026` in product UI unless a specific external/export contract requires it.
- When time is materially relevant, use `formatDateTime()` (or the compatibility helper that delegates to it); do not create a feature-local timestamp format.
- Machine values are not presentation: API/ISO serialization and HTML date-input values may keep the formats required by their contracts.
- New shared presentation formats require example-based regression coverage. Add number/currency/text helpers only when a concrete repeated product convention exists; do not create speculative wrappers.
- `npm run check:presentation-formatting` enforces the locale-sensitive formatting boundary during frontend builds. Do not weaken or bypass the guard to make a feature pass.

Preserve accessibility, responsive/mobile behaviour, loading/disabled/empty/error/recovery states, duplicate-submit protection, browser navigation and PWA safe-area behaviour.

## Playwright selector contract

- Playwright must use stable DOM `id` attributes as the interaction and assertion contract for product UI elements.
- Do not locate or validate product controls through user-facing copy, translated text, placeholders, labels, accessible names or button text (`getByText`, `getByPlaceholder`, text-based `getByRole`, etc.). Product copy is allowed to change without breaking browser automation.
- When a changed browser journey needs a control that has no stable `id`, add a meaningful stable `id` to the production component in the same cohesive change and target that ID from Playwright.
- IDs may include stable domain identifiers such as job/document/image IDs when needed to disambiguate repeated controls. Do not use array positions, generated CSS classes or transient text as the selector contract.
- Visible-copy correctness belongs in focused component/unit/accessibility validation when it is itself the changed requirement; it must not be used as Playwright navigation/synchronization plumbing.
- Existing Playwright flows that still depend on UI copy are test debt. Do not copy that pattern into new tests; migrate affected selectors to IDs when touching those flows.
- Decide the browser interaction points while implementing the component, before writing or extending the Playwright scenario. Stable IDs are part of the implementation contract, not post-hoc test plumbing.

## Playwright fixture boundary

- Create deterministic synthetic fixtures through the existing Development-only API/seed helpers by default. Use API reads for authoritative persisted-state assertions when that is stronger than reading presentation text.
- Keep the real UI for the user behaviour whose regression is being tested. API fixture setup must not replace the changed click/form/navigation/state transition that the browser scenario claims to prove.
- Do not navigate through unrelated UI merely to manufacture prerequisite data. A failure in fixture setup must not mask the user journey under test.
- Use UI fixture creation only when creating that fixture is itself part of the changed user journey being validated.
- Keep all fixture data synthetic, tenant-scoped and disposable under the existing ephemeral Playwright boundary.

## Browser validation sequencing

- Keep implementation PRs in draft while product code, stable IDs, focused regression tests, lint/typecheck and production build are still changing.
- Treat `Ready for review` as the browser-evidence code-freeze point. Expensive authenticated Playwright evidence belongs after the implementation/testability review, not during every implementation iteration.
- If implementation must change after browser evidence has started, normally convert the PR back to draft before editing. When it becomes ready again, CI reruns browser evidence on the new exact head.
- Do not manually treat an older browser run as evidence for a newer SHA. The exact-head `CI Gate` is the runtime source of truth for merge readiness.
- PR bodies declare stable browser intent with `Browser-Evidence`, `Browser-Scenarios`, `Browser-Scripts` and `Browser-Viewports`; mutable pass/pending/error bookkeeping comes from CI rather than repeated manual PR-body edits.
- `Browser-Scripts` maps every inferred flow to a concrete `playwright-*.mjs` script registered in `scripts/run-playwright-ephemeral.sh` (for example `job-wizard=playwright-critical-job-lifecycle.mjs`). If the relevant scenario is not in that exact-head runner, add/register the focused scenario before claiming browser coverage.

## Generated API and performance

- Endpoint source/OpenAPI define the API contract; do not hand-edit generated clients or models.
- Regenerate clients through the established process after contract changes.
- Watch for eager rare-feature imports, duplicate requests, unstable query keys, avoidable rerenders and tenant-unsafe service-worker/cache behaviour.
- Do not optimize for Lighthouse at the expense of actual primary-route interaction speed.

## Validation delta

Follow [`../../Docs/agents/VALIDATION.md`](../../Docs/agents/VALIDATION.md). Any user-visible frontend change requires the relevant Playwright flow against a running application before merge readiness; mobile-sensitive work also requires a narrow viewport.