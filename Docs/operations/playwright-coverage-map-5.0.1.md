# Release 5.0.1 Playwright coverage map

**Status:** Active stabilization inventory

**Owner:** WOR-705 / Workslip release maintainers

This map prioritizes browser tests by user impact and by how unlikely the flow is to be exercised during ordinary manual acceptance. It distinguishes the blocking ephemeral PR lane from the broader maintained critical harness.

## Blocking ephemeral PR lane

| Flow | Risk it protects | Current coverage |
| --- | --- | --- |
| Authenticated bootstrap, reload and logout | broken session persistence / logout | Blocking |
| Missing token and rejected token | unauthorized shell flash / bad redirect | Blocking |
| Transient `/api/auth/me` startup failure + retry | deployment warmup incorrectly destroys a valid login | **Blocking in WOR-705 wave 1** |
| User permission boundaries | ordinary User reaching admin/auditor/customer-edit surfaces by deep link | **Blocking in WOR-705 wave 1** |
| Auditor permission boundaries | Auditor reaching operational/admin surfaces, or shell exposing forbidden actions | **Blocking in WOR-705 wave 1** |
| Global Search / Quick Navigator | cross-feature search, mobile/desktop keyboard behavior | Blocking |
| Document attachment upload | frontend → API → storage contract and large upload behavior | Blocking |
| Brand day/night responsive contract | token/palette regressions | Blocking |
| Shared focus/state semantics | focus ring, selection/info/action semantics and overflow | Blocking |

## Maintained critical flows that are not yet part of every PR

These already have scenario implementations in the broader critical harness, but the normal ephemeral PR gate does not run them today:

| Flow | Why it is critical / rarely manually tested | Next action |
| --- | --- | --- |
| `kls-lifecycle` | full create → complete → submit → review/approve contract | Adapt to disposable synthetic auth and promote as release/blocking scenario |
| `rejection-loop` | reject → user recovery → resubmit → approval | Promote; high regression value because it crosses roles and notifications |
| `draft-recovery` | unfinished work survives navigation/reload | Promote after deterministic fixture review |
| `role-tenant-isolation` | security boundary that normal happy-path testing rarely exercises | Keep as explicit high-priority release evidence; migrate to ephemeral where feasible |
| `assignment-lifecycle` | duplicate-per-assignee independence and role visibility | Integrate with disposable seeded User/Admin identities |
| `customer-lifecycle` | search/favorite/edit/delete plus job snapshot retention | Promote after fixture cleanup is deterministic |
| `worksheet-integrity` | Danish decimal comma, edit/delete and duplicate prevention | Promote; directly protects timer/payroll data integrity |
| `diverse-lifecycle` | non-KLS simple job lifecycle | Promote after KLS lifecycle to protect second job type |
| `invitation-onboarding` | invalid invite recovery + Microsoft handoff | Keep focused/release-level because external Entra completion cannot be made hermetic |

## High-value gaps without sufficient browser coverage yet

1. **Notifications and rejection inbox** — unread count, opening the correct rejected job, comment visibility, read/unread persistence and mobile drawer behavior.
2. **Job validation/error routing** — submitting an incomplete flow must take the user to the exact missing field/step instead of only showing a generic error.
3. **Auditor deep-link authorization** — navigation hides worksheets for Auditor, but `/app/timer` currently lacks the same route-level `worksheet:view` guard used by the permission model. This is a discovered boundary inconsistency and must be fixed before adding a passing direct-link assertion.
4. **Related/duplicated jobs** — each assignee must only mutate their own copy; related-case navigation must not leak permissions or state.
5. **Back-navigation and scroll restoration** — open job/customer from Overview/Search, return to origin and preserve the expected list/filter/scroll position.
6. **Offline/reconnect/PWA mutations** — browser refresh/reconnect around an in-progress mutation must not duplicate submit or silently lose state.
7. **Empty/error states** — API 403/404/409/500 presentation and retry paths are less exercised than happy paths and should be tested where the user has a recovery action.

## Promotion rule

A flow should move into blocking PR Playwright when all are true:

- a regression would be user-visible, security-sensitive, or data-integrity relevant;
- the behavior crosses browser/UI and API/state boundaries, so unit/API coverage alone is insufficient;
- the fixture can run deterministically on the disposable Development SQL stack;
- the scenario can avoid arbitrary sleeps and use observable UI/network state;
- page errors and unexpected console errors are treated as failures.

Do not move external-provider-only behavior into the blocking ephemeral lane merely to increase scenario count. Keep provider/network-specific verification in a dedicated release or operator lane.
