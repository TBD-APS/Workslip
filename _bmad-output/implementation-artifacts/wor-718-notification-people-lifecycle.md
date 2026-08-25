---
title: 'WOR-718 notification + people lifecycle release evidence'
type: 'chore'
created: '2026-08-25'
status: 'done'
baseline_commit: '977d8b212d5af1c058a1db0ccc3172d2447edf75'
context:
  - '{project-root}/AGENTS.md'
  - '{project-root}/src/FE/AGENTS.md'
  - '{project-root}/Docs/agents/VALIDATION.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** WOR-718 har allerede et registreret Playwright-script, men notification-delen beviser kun en assignment-notifikation. Den mangler den beskrevne rejection-kontrakt, stabil selector-dækning, unread badge/count, korrekt navigation, read-state efter reload og Auditor-permissions; Linear og coverage-map overvurderer derfor release-evidensen.

**Approach:** Bevar den eksisterende people assignment/unassignment-flow, men gør notification-flowet deterministisk med syntetiske Diverse-sager, der går Draft → InReview → Rejected. Tilføj kun de stabile DOM-id'er, testen behøver, og bevis desktop/mobile UI, persisted API-state, permission-negative adfærd og rene browserdiagnostikker på den aktive `release-5.1`-linje.

## Boundaries & Constraints

**Always:** Arbejd på en `rbj-718-*` branch fra `origin/release-5.1`; målret kun PR mod `release-5.1`; brug disposable Development-data og eksisterende ephemeral runner; brug stabile DOM-id'er som Playwright-kontrakt; behandl page errors, console errors og uventede API-fejl som failures; ryd syntetiske jobs op i `finally`; rapportér kun exact-head evidens.

**Ask First:** Produkt-/API-adfærd ud over additive selector-id'er; ændring af permission-matrixen; enhver merge/deploy/release; fortsættelse uden Kimi kræver repository-ownerens eksplicitte midlertidige undtagelse, fordi `MOONSHOT_API_KEY` ikke er tilgængelig i denne session.

**Never:** Target eller merge til `main`; one-off workflow; tekst/copy-baserede Playwright-selectors; produktionsdata eller eksterne identiteter; skjule flakiness med reruns/sleeps; lukke Linear før exact-head gate er dokumenteret.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Rejection inbox | User-ejet Diverse-sag afvises af Admin | User får én ulæst rejection-notifikation; badge/count matcher API; klik markerer læst og åbner korrekt sag | Manglende/duplikeret event, forkert URL eller stale read-state fejler scenariet |
| Reload persistence | Notifikation er åbnet/læst | Reload henter server-state; event forbliver læst og unread count falder | UI/API-drift fejler scenariet |
| Permission boundary | Auditor-session | Bell/drawer er ikke eksponeret; notifications API afviser adgang | Eksponeret control eller tilladt API-kald fejler scenariet |
| People lifecycle | Admin detail-view på seeded User | Detaildata vises; assign/unassign persisterer på desktop/mobile; direkte User-URL afvises | Cross-role control, stale assignment eller browser/API-fejl fejler scenariet |

</frozen-after-approval>

## Code Map

- `src/FE/scripts/playwright-notification-people-lifecycle.mjs` -- eksisterende fokuseret scenario og primær implementeringsflade.
- `src/FE/src/components/common/NotificationsDrawer.tsx` -- stabile id'er til unread state og notification actions.
- `src/FE/src/components/layouts/AppLayout.tsx` -- eksisterende bell og unread badge.
- `src/FE/src/features/jobs/routes/AdminCompletedJobReport.tsx` -- stabil root-selector, der beviser at rejection-deep-linket renderer den korrekte sag.
- `src/FE/src/components/common/NotificationsDrawer.test.tsx` -- komponentregression for selector-kontrakten uden copy-afhængighed.
- `src/FE/scripts/run-playwright-ephemeral.sh` -- eksisterende blocking registration; skal verificeres, ikke duplikeres.
- `Docs/operations/playwright-coverage-map-5.0.1.md` -- stale gap/owner-tekst skal bringes i sync med den faktiske aktive runner.

## Tasks & Acceptance

**Execution:**
- [x] `NotificationsDrawer.tsx`, `AppLayout.tsx` og drawer-test -- tilføj meningsfulde stabile id'er til count, badge, mark-read og notification-row actions uden at ændre produktadfærd.
- [x] `playwright-notification-people-lifecycle.mjs` -- erstat assignment-notification fixture med rejection fixture; assertér badge/count, korrekt jobnavigation, persisted read/reload og Auditor-denial på desktop/mobile; tilføj failed-response diagnostics og deterministic IDs.
- [x] `playwright-coverage-map-5.0.1.md` -- markér notification/people flow som blocking på aktiv runner og fjern den nu lukkede notification-gap-påstand.

**Acceptance Criteria:**
- Given en synthetic User-sag, when User indsender og Admin afviser, then den registrerede ephemeral flow beviser rejection inbox, unread state, korrekt deep-link og persisted read-state på desktop og mobile.
- Given en Auditor-session, when app-shell og notifications API undersøges, then UI-control er skjult og API-adgang afvises.
- Given den eksisterende people fixture, when Admin assigner/unassigner gennem User detail, then state persisterer efter reload, User kan ikke deep-linke til controls, og ingen uventede browser/API-fejl forekommer.
- Exact-head frontend tests/build, selector-contract, docs check og blocking ephemeral Playwright skal være grønne før PR kan være READY.

## Spec Change Log

- 2026-08-25: Implementerede den godkendte WOR-718-plan. Den frosne intent-/boundary-/edge-case-blok er uændret.

## Verification

**Commands:**
- `npm test -- --run` fra `src/FE` -- samlet frontend-regression.
- `npx tsc -b && npm run typecheck:sw && npm exec vite build` fra `src/FE` -- compilering og produktionsbundle uden den kendte repository-budgetprecheck.
- `node tools/release/validate-playwright-selector-contract.mjs --base 977d8b212d5af1c058a1db0ccc3172d2447edf75 --head HEAD` -- exact-head selector-kontrakt efter commit.
- `python tools/docs/check_docs.py` -- maintained docs på en ren checkout uden fremmede worktree-artefakter.
- Canonical ephemeral runner/CI på exact PR head -- `playwright-notification-people-lifecycle.mjs` passerer desktop/mobile mod disposable SQL/API/Vite stack.

**Implementation evidence (2026-08-25):**

- Focused component contract: 9/9 tests passeret.
- Samlet frontend-suite: 87 testfiler og 394 tests passeret.
- Targeted ESLint for de tre ændrede TSX-filer: passeret.
- TypeScript project build, service-worker typecheck og direkte Vite production build: passeret.
- Exact-head selector-kontrakt og selector-validator unit tests: passeret (7/7).
- Eksisterende blocking registration i `run-playwright-ephemeral.sh`: verificeret; ingen duplicate runner tilføjet.
- `node --check` og `git diff --check`: passeret.
- Det samlede `npm run lint` er blokeret af 44 eksisterende fejl i uberørte filer; de ændrede TSX-filer er lint-grønne isoleret.
- Det samlede `npm run build` stopper i den eksisterende `App.css`-budgetgate (122862 > 117000 bytes); selve `tsc -b`, service-worker typecheck og Vite build passerer.
- Docs-checket er blokeret af tre eksisterende `repomix`-artefakter i `.worktrees/portability-report`, ikke af denne dokumentationsændring.
- Det fokuserede authenticated Playwright-scenario passerede lokalt mod Development API/SQL og working-tree Vite på desktop/mobile, inklusive rejection-rendering, read/reload, Auditor 403 og people assign/unassign.
- Den fulde canonical ephemeral CI-runner er fortsat nødvendig på exact PR-head før READY.
- Repository-ejeren godkendte eksplicit den midlertidige Kimi-undtagelse i denne session, fordi `MOONSHOT_API_KEY` ikke er tilgængelig; undtagelsen skal også fremgå af PR-beskrivelsen.

## Suggested Review Order

**End-to-end lifecycle**

- Entry point coordinates real rejection, permissions, persistence, diagnostics, and cleanup.
  [`playwright-notification-people-lifecycle.mjs:330`](../../src/FE/scripts/playwright-notification-people-lifecycle.mjs#L330)

- Rejection correlation prevents unrelated same-job notifications from satisfying the contract.
  [`playwright-notification-people-lifecycle.mjs:297`](../../src/FE/scripts/playwright-notification-people-lifecycle.mjs#L297)

- People detail remains the preserved assignment/unassignment coverage across both viewports.
  [`playwright-notification-people-lifecycle.mjs:180`](../../src/FE/scripts/playwright-notification-people-lifecycle.mjs#L180)

**Stable UI contracts**

- Drawer exposes deterministic count, row, action, and unique grouped selectors.
  [`NotificationsDrawer.tsx:335`](../../src/FE/src/components/common/NotificationsDrawer.tsx#L335)

- Shell badge exports its unread value without changing visible behaviour.
  [`AppLayout.tsx:170`](../../src/FE/src/components/layouts/AppLayout.tsx#L170)

- Rendered job root proves the notification opens the actual rejection report.
  [`AdminCompletedJobReport.tsx:231`](../../src/FE/src/features/jobs/routes/AdminCompletedJobReport.tsx#L231)

**Regression evidence**

- Component tests protect unread state and grouped action selector uniqueness.
  [`NotificationsDrawer.test.tsx:152`](../../src/FE/src/components/common/NotificationsDrawer.test.tsx#L152)

- Coverage inventory records both flows as blocking desktop/mobile evidence.
  [`playwright-coverage-map-5.0.1.md:22`](../../Docs/operations/playwright-coverage-map-5.0.1.md#L22)
