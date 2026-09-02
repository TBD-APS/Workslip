# Entra tenant migration

How Workslip moves to a new Entra tenant, and what has to be recreated rather than copied.

## Status and tooling

The `mrsoftware` → `mrsoftwarev2` move is **done**; the `live` tenant is the one
serving customers. This page stays as the explanation of *why* the object-ID
problem exists and how to reason about it, which applies to any future move.

For the live tenant, prefer the workflows over running scripts by hand — they
carry the protected-environment approval and the typed confirmation:

| Task | Entry point |
|---|---|
| Reconcile guests, app roles and Entra object IDs in `live` | `Entra · Reconcile Workslip live B2B guests` (`aca-live-entra-guests.yml`), `mode: report` then `apply` with `RECONCILE_GUESTS` |
| Move B2B guests across tenants | `src/BE/infrastructure/aca/migrate-live-b2b-guests-between-tenants.ps1` |
| Object-ID backfill for a non-live environment or a fresh tenant | `src/BE/infrastructure/backfill-entra-object-ids.ps1` |

`reconcile-live-entra-guests.ps1` is the implementation the workflow calls; it is
not a separate operator entry point. The `report` mode writes nothing, so it is
the safe first move in every case.

## What survives and what does not

Azure resources are redeployed from `main.bicep`. Application data is cloned. Neither of
those is the risk.

The risk is Entra. Every directory object — users, app registrations, groups, managed
identities — is scoped to one tenant. Recreating a user in another tenant always mints a
new object ID, so `Users.EntraId` in the Workslip database points at objects that do not
exist in the new tenant.

Sign-in survives this. `EfUserRepository.GetByExternalIdentityAsync` matches in priority
order:

| Priority | Match |
|---|---|
| 0 | `EntraId` equals the `oid` claim |
| 1 | `EntraEmail` matches an email claim |
| 2 | `Email` matches an email claim |

Email candidates come from `email`, `preferred_username`, `upn`, `unique_name` and both
`ClaimTypes` equivalents, and guest UPNs of the form `user_domain.dk#EXT#@tenant` are
decoded back to the underlying address. A user whose address is unchanged can therefore
sign in with a stale `EntraId`.

What does not survive is everything keyed on the object ID:

- `IUserEntraService.DeleteUserAsync` — offboarding calls Graph with a dead ID, so the
  directory account is never removed
- `ISuperadminEntraService.RevokeSuperadminAsync` — same, for superadmin revocation
- `IsEntraIdentityReferencedAsync` — the guard against two Workslip users sharing one
  directory identity compares against dead IDs

Sign-in works; offboarding silently does nothing. Run the backfill before treating the
migration as complete.

## Tenant-bound values

Three values are per-tenant. Two are resolved automatically, one is not.

| Value | Source | Behaviour in a new tenant |
|---|---|---|
| `Azure:AdOAuth:TenantId` | `az.tenant().tenantId` in `staticConfig.bicep` | Follows the deployment automatically |
| `Azure:AdOAuth:ClientId` | `EntraAppRegistrations.outputs.OAuthClientId` | Follows the deployment automatically |
| `Azure:AdOAuth:Domain` | `entraDefaultDomain` parameter | Resolved from Graph by `deploy-infrastructure.ps1`, or passed with `-EntraDefaultDomain` |

`Azure:AdOAuth:Domain` matters more than it looks.
`UserEntraService.FindExistingEntraUserAsync` builds `userPrincipalName` as
`<mailNickname>@<domain>` when it checks whether a directory user already exists. An
empty or foreign domain makes that filter match nothing, and the service creates a
duplicate account instead of reusing the real one. It had no infrastructure source
before this change and had to be set by hand in App Configuration.

`globalAdminId` is also tenant-bound. Its default in `deploy-infrastructure.ps1` is an
object ID from the original production tenant. The script now verifies that the ID
exists in the signed-in tenant and falls back to the deploying principal when it does
not, so the old tenant keeps its current behaviour and a fresh tenant works without an
argument.

## Order of operations

0. **Preview.** `./plan.ps1 <env>` — reports what all four phases would do and changes
   nothing. In a tenant where the Entra phase has not run yet, phases 1 and 2 stop early
   and say the registrations are absent; that is the expected first result, not a fault.
   See `src/BE/infrastructure/README.md` for what the preview can and cannot see.
1. **Deploy infrastructure.** `./deploy-infrastructure.ps1 <env>` — resolves the default
   domain and global administrator, then deploys. Both are resolved before the first
   mutation, so a wrong tenant fails immediately instead of halfway through.
2. **Create the users.** Provisioning is idempotent: `FindExistingEntraUserAsync` looks
   up `mail`, `otherMails`, `userPrincipalName` and the guest UPN prefix before
   creating, so the run can be repeated safely.
3. **Check the mapping.** `./backfill-entra-object-ids.ps1 -Environment <env>` — reports
   only, writes nothing.
4. **Write the mapping.** Re-run with `-Apply` once the report looks right.

## Reading the backfill report

| Status | Meaning |
|---|---|
| `Current` | Database already holds the correct object ID |
| `Backfill` | Directory account found, database ID missing or stale — will be written |
| `Missing` | No directory account matches this address. The user cannot sign in and cannot be offboarded through Graph until one exists |
| `Ambiguous` | The address resolves to more than one directory account. Resolve by hand |
| `Conflict` | Two Workslip users resolve to the same directory account, which the application forbids |
| `NoEmail` | Row has neither `Email` nor `EntraEmail` |

`Backfill` rows are written in one transaction. A partial write would leave some rows
pointing at the new tenant and some at the old one, which is worse than not starting.

Only `Backfill` rows are ever written. Everything else is reported and skipped, so the
script is safe to re-run.

## Verifying before go-live

Confirm in the new tenant that:

- `Azure:AdOAuth:Domain` in App Configuration equals the tenant's default verified domain
- One user can sign in — this exercises the email fallback
- The backfill dry run reports zero `Missing`, `Ambiguous` and `Conflict` rows
- After `-Apply`, a re-run reports every user as `Current`
- Deleting a test user through Workslip actually removes the directory account — this is
  the check that proves the backfill worked, since it is the path that silently does
  nothing when `EntraId` is stale
