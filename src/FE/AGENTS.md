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

Preserve accessibility, responsive/mobile behaviour, loading/disabled/empty/error/recovery states, duplicate-submit protection, browser navigation and PWA safe-area behaviour.

## Generated API and performance

- Endpoint source/OpenAPI define the API contract; do not hand-edit generated clients or models.
- Regenerate clients through the established process after contract changes.
- Watch for eager rare-feature imports, duplicate requests, unstable query keys, avoidable rerenders and tenant-unsafe service-worker/cache behaviour.
- Do not optimize for Lighthouse at the expense of actual primary-route interaction speed.

## Validation delta

Follow [`../../Docs/agents/VALIDATION.md`](../../Docs/agents/VALIDATION.md). Any user-visible frontend change requires the relevant Playwright flow against a running application; mobile-sensitive work also requires a narrow viewport.
