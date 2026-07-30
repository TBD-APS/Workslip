# Workslip frontend instructions

Read the root `AGENTS.md`, `Docs/agents/OPERATING_CONTRACT.md`, and `Docs/agents/VALIDATION.md` before changing frontend code.

## Scope

These rules apply to `src/FE/`.

## Architecture and implementation

- Reuse established feature folders, shared components, query helpers, and generated/shared API clients.
- Do not bypass the established API client with ad hoc `fetch` or a separate Axios instance.
- Keep server state in React Query. Do not duplicate it into local state unless editing requires an explicit draft.
- Avoid unnecessary effects, derived-state synchronization, and provider expansion.
- Split oversized components when the split creates a real responsibility boundary.
- Lazy-load genuinely rare routes or features when it improves initial application behavior without making navigation noticeably worse.
- Keep authorization decisions on the backend. Frontend guards are presentation and navigation controls only.
- Tenant-, user-, and session-sensitive query keys must include the context required to prevent cache leakage.
- Clear or invalidate relevant state when authentication or organization context changes.

## Shared form and UI components

Use components under `src/FE/src/components/forms/` and other existing shared UI before creating new controls.

Never use raw `<input type="number" />`. Use `NumericInput` because mobile browsers can strip Danish decimal commas from native number inputs.

```tsx
import { NumericInput } from './components/forms/NumericInput';

<NumericInput
  kind="decimal"
  min={0}
  value={value}
  onChange={setValue}
/>
```

Follow existing normalization helpers such as the `parseHours` pattern when converting display values.

Preserve:

- labels, accessible names, focus order, keyboard operation, and visible focus;
- mobile and narrow-screen behavior;
- loading, disabled, empty, success, error, and recovery states;
- duplicate-submit protection;
- browser-back and route restoration behavior;
- stale-token and reauthentication handling;
- safe-area behavior for installed PWA usage.

## API and generated artifacts

- Treat endpoint source and OpenAPI as the contract source.
- Do not hand-edit generated API clients or generated models.
- Regenerate clients through the established process after contract changes.
- Keep mutations idempotent where the backend contract supports it.
- Surface actionable errors without exposing sensitive backend details.

## Performance review

Review frontend changes for:

- unnecessary bundle growth;
- eager imports of rare functionality;
- duplicate requests;
- unstable query keys;
- avoidable rerenders and effects;
- large lists without virtualization or bounded pagination where scale requires it;
- service-worker or caching changes that can serve stale authentication or tenant data.

Do not optimize solely for Lighthouse. Preserve actual interaction speed after login and on primary routes.

## Required validation

Any user-visible frontend change requires Playwright against a running application. This includes visual changes, inputs, dialogs, buttons, routing, authentication, session handling, notifications, responsive behavior, and error recovery.

At minimum run:

- frontend lint for the affected code;
- TypeScript checking;
- production frontend build;
- the relevant Playwright flow from `Docs/agents/VALIDATION.md`.

For mobile-sensitive controls, include a narrow mobile viewport. For authentication, routing, or session changes, verify redirects, browser back, reload, console errors, and network failures.

A user-visible frontend PR without successful Playwright validation is **implemented but Playwright-unvalidated** and should remain draft or explicitly blocked.
