---
title: 'WOR-252: Keep invitation controls visible on narrow screens'
type: 'bugfix'
created: '2026-08-01'
status: 'done'
baseline_commit: 'd6c1c3c59459269d43f277d68a041495d9fbf9de'
context:
  - '{project-root}/Docs/agents/VALIDATION.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The invitation-role addition made the settings UI too dense: the role dropdown exposes internal English role names, while long invitation e-mails plus status, role, date, and delete action can push the trash button outside a narrow mobile row.

**Approach:** Show only Danish role names in the dropdown, use the compact `Medarb.` label in the status list, and make each invitation row reserve a stable action column. The e-mail may shorten visually with an ellipsis when space is limited, while the full role/e-mail values remain available via title and accessibility text.

## Boundaries & Constraints

**Always:** Preserve the API values `User` and `Auditor`; keep e-mail, status, role, date, and clear-invitation action available; keep the delete button's accessible name, disabled state, confirmation, spinner, success/error feedback, and minimum touch target; support narrow installed-PWA and desktop layouts without horizontal page overflow.

**Ask First:** Removing metadata, changing its meaning/order, changing the clear-invitation workflow, or expanding the redesign beyond invitation controls in Settings.

**Never:** Change backend/API contracts, generated clients, invitation authorization, or mutation behavior; add a UI dependency; hide the delete action; destructively shorten the stored or submitted e-mail address.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|---------------|---------------------------|----------------|
| Role selection | Admin opens the invitation-role dropdown | Options read `Medarbejder` and `Auditør`; selected values remain `User` and `Auditor` in the invitation request | Existing mutation error handling remains unchanged |
| Long invitation | A narrow viewport renders a long e-mail with status, role, date, and trash action | `Medarbejder` displays compactly as `Medarb.`; e-mail is visually ellipsized as needed; trash stays fully visible and tappable | Full role and e-mail remain available to assistive technology and via tooltip |
| Ordinary invitation | Desktop or sufficient width | Existing single-row scan order remains compact and all metadata stays visible | N/A |
| Clear pending | One invitation is being cleared | Its spinner remains in the reserved action position; action layout does not jump | Existing disabled/error/recovery behavior remains unchanged |

</frozen-after-approval>

## Code Map

- `src/FE/src/features/settings/routes/Settings.tsx` -- renders the role selector and invitation status rows; currently contains verbose option labels and an inline action wrapper.
- `src/FE/src/App.css` -- owns invitation row sizing, e-mail truncation, badges, date, and responsive layout.
- `src/FE/src/features/settings/routes/Settings.invitation-role.test.tsx` -- protects role labels and the unchanged API role value.

## Tasks & Acceptance

**Execution:**
- [x] `src/FE/src/features/settings/routes/Settings.tsx` -- shorten dropdown/list role names, give row/action elements stable classes, and expose full display values without changing submitted data.
- [x] `src/FE/src/App.css` -- reserve the trash-action column, constrain the content column, and wrap invitation metadata safely on narrow screens while retaining the desktop row.
- [x] `src/FE/src/features/settings/routes/Settings.invitation-role.test.tsx` -- verify short user-facing labels and unchanged `Auditor` request value.

**Acceptance Criteria:**
- Given the role dropdown, when it opens, then it shows only `Medarbejder` and `Auditør` while sending the existing backend enum value.
- Given a status row for a `User` invitation, when it renders, then the compact badge reads `Medarb.` and exposes the full `Medarbejder` label.
- Given a 320–390px viewport and a long invitation e-mail, when the status list renders, then the row has no horizontal overflow and the trash button remains fully visible and operable.
- Given a desktop viewport, when the same invitation renders, then the row remains compact, readable, and functionally unchanged.

## Spec Change Log

## Design Notes

The action column must be structurally reserved rather than rescued with hard-coded e-mail character counts. CSS truncation adapts to actual viewport, font, badge, and translated-label widths while preserving the real address.

## Verification

**Commands:**
- `./node_modules/.bin/eslint src/features/settings/routes/Settings.tsx src/features/settings/routes/Settings.invitation-role.test.tsx` -- expected: no errors or new warnings.
- `./node_modules/.bin/vitest run src/features/settings/routes/Settings.invitation-role.test.tsx` -- expected: focused invitation behavior passes.
- `npm run build` -- expected: TypeScript, service-worker typecheck, Vite, and PWA production build pass.

**Manual checks (if no CLI):**
- Run the production PWA in Chromium at 320x700, 390x844, and 1280x900; open Settings, exercise the role dropdown and real trash control, verify full button bounds inside the row, no horizontal overflow, stable pending feedback, and no console or network errors.

**Recorded results (2026-08-01):**
- Focused Vitest: 2 tests passed, including Danish option labels, unchanged `Auditor` submission, compact `Medarb.` display, visually hidden full role text, and the complete e-mail value.
- Targeted ESLint: passed without errors or warnings.
- Production build: TypeScript, service-worker typecheck, Vite, and injectManifest PWA build passed; only the pre-existing Vite `inlineDynamicImports` deprecation warning remained.
- Playwright-backed Chromium production-PWA checks passed at 320x700, 390x844, and 1280x900 with the real built service worker: no page/row overflow, the delete button stayed inside the row at 44x44px, and the long e-mail was visually ellipsized while its full value remained in the DOM and tooltip.
- Dropdown interaction selected `Auditør` with the unchanged value `Auditor`.
- The real trash action completed the existing confirmation/delete/refresh flow against a delayed response (`DELETE /api/auth/invites/{id}` followed by `GET /api/auth/invites`), exercising the pending action position and refreshed visible state.
- Browser console and network traffic were inspected. No application errors or failed/duplicated invitation requests occurred; the isolated validation harness produced only the expected notification-permission warning.

## Suggested Review Order

**Invitation presentation**

- Start with the rendered row: compact labels, complete accessible text, and reserved action placement.
  [`Settings.tsx:232`](../../../src/FE/src/features/settings/routes/Settings.tsx#L232)

- Confirm the selector keeps backend values while presenting Danish-only role names.
  [`Settings.tsx:140`](../../../src/FE/src/features/settings/routes/Settings.tsx#L140)

**Responsive layout**

- The two-column grid structurally protects the action from long invitation content.
  [`App.css:4823`](../../../src/FE/src/App.css#L4823)

- Mobile wrapping and the 44px action target prevent narrow-screen overflow.
  [`App.css:4895`](../../../src/FE/src/App.css#L4895)

- Visually hidden full role text avoids unreliable naming on generic elements.
  [`App.css:4860`](../../../src/FE/src/App.css#L4860)

**Regression protection**

- Tests preserve API enum values and verify compact, accessible invitation metadata.
  [`Settings.invitation-role.test.tsx:70`](../../../src/FE/src/features/settings/routes/Settings.invitation-role.test.tsx#L70)

**Deferred repository maintenance**

- Existing repomix drift stays isolated from this cohesive feature change.
  [`deferred-work.md:21`](deferred-work.md#L21)
