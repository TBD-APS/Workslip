# CLAUDE.md

Orientation for AI assistants (Claude Code and others) working in this repository.

This file explains **what the codebase is, how it is organized, how to run and validate it, and the conventions that matter**. It is a map, not the rulebook.

> **The authoritative agent rules live in [`AGENTS.md`](AGENTS.md) and the scoped `AGENTS.md` files.** Read the root `AGENTS.md` plus the closest scoped one before changing code. This file deliberately does **not** restate those rules; the repository treats duplicated guidance as drift (see [`Docs/AGENTS.md`](Docs/AGENTS.md) and `tools/docs/check_docs.py`). When this map and the code disagree, the code wins — fix the map.

## What Workslip is

Workslip is a multi-tenant SaaS for field-service / job management (Danish-market product; UI copy and some domain terms such as **Filial** are Danish). It has three surfaces:

- a **React + Vite PWA** frontend (`src/FE/`),
- an **ASP.NET Core .NET 10 API** (`src/BE/WorkslipApi/`),
- **Azure/Entra infrastructure** and database migrations (`src/BE/infrastructure/`),

plus a public marketing site (`site/`, Jekyll) and maintained documentation (`Docs/`).

## Repository map

```text
src/FE/                     React 19 + TypeScript + Vite PWA frontend
src/BE/WorkslipApi/         .NET 10 API: host + Application/Domain/Infrastructure + Tests
src/BE/infrastructure/      Azure/Entra provisioning, deployment scripts, DB migrations
Docs/                       Maintained architecture, API, operations, compliance docs + ADRs
tools/                      Repo maintenance & validation tooling (docs, release, git, depmap, playwright)
site/                       Public marketing site (Jekyll / GitHub Pages)
.github/                    CI workflows, AI PR review, PR template
AGENTS.md                   Authoritative repository-wide agent rules (+ scoped AGENTS.md files)
README.md                   Repository entrypoint
dev.ps1 / docker-compose.yml  Canonical local full-stack bootstraps
```

## Tech stack (concrete)

| Layer | Stack |
|---|---|
| Frontend | React 19, TypeScript ~6, Vite 8, React Router 7, TanStack React Query 5, react-hook-form + Zod, Axios, `vite-plugin-pwa`, Application Insights. Node 24 recommended. |
| API host | ASP.NET Core minimal APIs on **.NET 10**, Serilog, FluentValidation, `Ardalis.Result`, `Microsoft.Identity.Web` + `Microsoft.Graph`, `Scalar`/OpenAPI, WebPush. |
| Persistence | EF Core 10 + **SQL Server**; explicit versioned SQL migrations (not EF `Migrations` at runtime). |
| Auth | Workslip local JWT **and** Microsoft Entra JWT. |
| Cloud | Azure App Service, App Configuration + Key Vault, Blob Storage, Application Insights; deployed via GitHub Actions with OIDC. Frontend on Vercel. |
| Telemetry/logs | Application Insights (prod), Seq (local docker-compose). |

## Backend architecture (`src/BE/WorkslipApi/`)

Clean-architecture layering; keep dependencies pointing inward.

```text
Workslip.Api          Host + minimal-API endpoints (Endpoints/), Program.cs, Configuration/, Middleware/, Services/
Workslip.Application  Use-case services, validators (FluentValidation), DTOs — returns Ardalis.Result
Workslip.Domain       Domain models / persistence rows (Models/)
Workslip.Infrastructure  EF Core (SqlDbContext), repositories, Graph/email/storage/notifications, mappers, seeding
Workslip.Tests        xUnit tests mirroring the above folders (Application/, Endpoints/, Infrastructure/, ...)
```

Key backend conventions (details in [`src/BE/WorkslipApi/AGENTS.md`](src/BE/WorkslipApi/AGENTS.md)):

- **Thin endpoints.** Business/workflow rules live in Application/Domain; integration details (EF, SQL, Graph, email, storage) live in Infrastructure.
- **`Ardalis.Result`.** Application services return `Result<T>` / `Result`; endpoints map via `ResultExtensions.ToHttpResult`. Don't invent competing result wrappers.
- **Authorization & tenancy are backend-owned.** Verify server-side policy, repository/query tenant scope, and cache/log isolation. Never derive tenant authority from client headers or frontend state.
- **Prove SQL behaviour with relational tests**, not EF in-memory — for translation, constraints, transactions, concurrency, cascade/orphan behaviour.
- The solution file is `Workslip.slnx`. `InternalsVisibleTo` exposes internals to `Workslip.Tests`.

## Frontend architecture (`src/FE/`)

Feature-folder architecture under `src/`:

```text
src/routes/       App routing + guards (ProtectedRoute, RoleGuard). Router in routes/index.tsx
src/features/     One folder per feature: auth, jobs, worksheets, customers, users, overview,
                  create, settings, superadmin, auditor, docs, images, legal.
                  Each feature typically has routes/ components/ hooks/ queries/ api/ utils/
src/components/   Shared UI: forms/, common/, layouts/, filters/, pagination/
src/providers/    App/session providers (Auth, Theme, permissions/ with RoleGuard + Can)
src/hooks/        Reusable hooks (pagination, infinite scroll, debounce, media query, ...)
src/lib/          Transport & cross-cutting (axios.ts, react-query, query keys, formatting, toast, PWA events)
src/api/          fetcherOrval.ts (Axios mutator) + generated/ (Orval output — see below)
src/pwa/ sw.ts registerSW.ts   Service worker / PWA update behaviour
src/telemetry/ applicationInsights.ts   Frontend telemetry
```

Key frontend conventions (details in [`src/FE/AGENTS.md`](src/FE/AGENTS.md)):

- **Server state is React Query.** Local state is for UI/edit drafts only — don't duplicate server truth. Include user/tenant/session context in cache keys where isolation matters, and clear caches on session/tenant change.
- **Use the shared API client only.** No ad hoc `fetch` or extra Axios instances; the shared transport (`src/lib/axios.ts`, wired through `src/api/fetcherOrval.ts`) owns auth/correlation.
- **Generated API client** lives at `src/api/generated/` and is produced by Orval from the backend OpenAPI contract (`orval.config.ts`). It is generated by `npm run generate:api:local` (run automatically by `predev`). **Do not hand-edit it.** After a backend contract change, regenerate and commit; CI regenerates a branch-matched client and fails if `src/api/generated` is left dirty or drifts from the same-revision backend.
- **Use `NumericInput`** (in `src/components/forms/`) instead of raw `<input type="number">` — native number inputs can lose Danish decimal-comma entry.
- **Frontend guards are UX only.** `RoleGuard`/`Can` control navigation/presentation; the security boundary is the backend.
- Preserve accessibility, responsive/mobile + PWA safe-area behaviour, and loading/disabled/empty/error/recovery states. UI copy is Danish.

## Domain & multi-tenancy

Authoritative sources: `SqlDbContext`, domain models, migrations, persistence tests, and [`Docs/architecture/domain-and-dataflows.md`](Docs/architecture/domain-and-dataflows.md).

- **`OrganizationId` is the tenant boundary** (server-owned). Authorization + repository filters are required; DB constraints are an *additional* integrity boundary, not a replacement for authorization.
- **Filial** = child of an Organization (not a tenant boundary). Every org has one default Filial. `Users` and `JobReports` carry `FilialId`; relationships use `(OrganizationId, FilialId)` composite keys so cross-org IDs can't be attached. (ADR 0007.)
- **`Role` vs `UserKind`** are separate: `Role` controls authorization; `UserKind` (`Member` / `InternalTest`) controls which user audience an identity belongs to. (ADR 0008 user-audience-separation.)
- **Superadmin** never removes ordinary tenant filtering — cross-org work uses an explicit delegated-organization session flow.
- Core entities (see `Workslip.Domain/Models/`): Organization, Filial, User, Customer, Job / JobReport (+ installation snapshot chain, closure flags, events, work kinds), Assignment, Worksheet, Notification (push/queue/delivery), PushSubscription, Idempotency records, reference/control data.
- Business-domain → module mapping and the boundary-split plan: [`Docs/architecture/domain-split-plan.md`](Docs/architecture/domain-split-plan.md).

## Local development

**Canonical (supported Windows dev machine):** from the repo root —

```powershell
.\dev.ps1            # full-stack bootstrap + smoke
.\dev.ps1 -Mobile    # also exposes the Vite dev server to the LAN and prints a phone URL
```

Details, prerequisites, firewall/secure-context notes: [`Docs/operations/local-development.md`](Docs/operations/local-development.md). Do not invent `appsettings.Local.json` values or route synthetic users through Entra just to make local dev start.

**Docker (cross-platform full stack):**

```bash
docker compose up      # fe :5270 · api :5262 · SQL :1433 · Seq :5341
```

**Manual, for focused debugging:**

```bash
# Frontend
cd src/FE && npm ci && npm run dev        # http://127.0.0.1:5270  (predev generates the API client)

# Backend  (needs a provably-local SQL target; startup fails closed on remote SQL)
cd src/BE/WorkslipApi && dotnet restore && dotnet run --launch-profile http   # http://localhost:5262
curl http://localhost:5262/health
```

Default local URLs: frontend `http://127.0.0.1:5270`, API `http://localhost:5262`, health `http://localhost:5262/health`. In Development, a local SQL target auto-applies pending versioned migrations before the connectivity check. Dev seeding is explicit opt-in (`Workslip:SeedDevelopmentData=true`); Entra reconciliation needs an additional flag. See [`src/BE/WorkslipApi/README.md`](src/BE/WorkslipApi/README.md).

## Common commands

| Task | Command |
|---|---|
| Backend build | `cd src/BE/WorkslipApi && dotnet build Workslip.slnx --configuration Release` |
| Backend tests | `cd src/BE/WorkslipApi && dotnet test Workslip.slnx --configuration Release` |
| Frontend install | `cd src/FE && npm ci` |
| Frontend lint | `npm run lint` |
| Frontend unit tests | `npm run test -- --run` (Vitest) |
| Frontend build | `npm run build` (tsc + service-worker typecheck + Vite; no remote OpenAPI fetch) |
| Regenerate API client | `npm run generate:api:local` (from backend OpenAPI built in this tree; no running API/DB needed) |
| Docs drift check | `python tools/docs/check_docs.py` |
| Postman/API (localhost/approved test only) | scripts in `src/BE/WorkslipApi/Postman/` |
| Playwright critical flows | see `tools/playwright/` and [`Docs/operations/playwright-critical-flows.md`](Docs/operations/playwright-critical-flows.md) |
| Dependency map | `node tools/depmap/depmap.mjs` |

`src/FE/package.json` is the authoritative frontend command list.

## Validation

Full policy: [`Docs/agents/VALIDATION.md`](Docs/agents/VALIDATION.md). Pick the **smallest evidence set that makes the changed risk believable** — don't maximize test count.

Three default regression tools:

1. **Unit** — calculations, business branching, important state transitions, deterministic edge cases.
2. **Postman feature/API** — primary backend feature verification across HTTP, authorization, persistence, multi-endpoint workflows. Runtime evidence requires *actually executing* against localhost/approved test — parsing the collection is not evidence.
3. **Playwright** — critical *changed* user-visible browser flows (auth/routing/session/cache/mobile).

Engineering gates (build, lint/typecheck, OpenAPI/client parity, migration/schema checks, `check_docs.py`) can be mandatory without adding product tests. Report evidence precisely — never a bare "works"/"validated"; state what actually ran and what remains unverified.

## Git & pull-request workflow

Governing rules: [`AGENTS.md`](AGENTS.md) → *Branch and scope discipline* / *Delivery loop*.

- **Never push to `main`.** `main` is the production boundary (ADR 0005).
- One Linear issue per implementation branch/PR. Branch: `rbj--<issue>-<description>`; PR title: `RBJ-<issue>: <description>`. Squash merge.
- **Prefer Git stacks** for related/ordered/overlapping work (child branch off the previous stack branch, child PR targets its parent). Don't open parallel PRs for the same delivery sequence.
- Keep PRs small and cohesive; don't mix unrelated cleanup into feature work.
- Repository-governance/docs changes explicitly requested by the owner may omit a Linear issue.
- Never commit secrets (connection strings, JWT signing keys, Azure creds, VAPID private keys, tokens, synthetic-identity emails) or production personal data.

> **This session's assigned branch is `claude/claude-md-docs-4u8gwc`** (per session instructions). Develop, commit, and push there; open a PR against `main` if none exists.

## CI & production boundary

Full model: [`Docs/operations/ci-quality-gates.md`](Docs/operations/ci-quality-gates.md).

- The unified CI workflow is `.github/workflows/frontend-validation.yml` (named **CI**); the merge signal is the **`CI Gate`** job, which requires: `Backend` (full Release build + backend suite), `Frontend + API contract` (ESLint ratchet, branch-matched OpenAPI/Orval parity, Vitest, prod build), and `Contracts + docs` (release-policy checks, Playwright source checks, Postman JSON validation, `check_docs.py`).
- The **full backend suite is blocking** — repair failing tests/code, don't skip or allowlist to go green.
- Production mutations are **fail-closed on the exact post-merge SHA**: a green ancestor is not enough; red/cancelled/stale/missing/duplicate gate = not deployable. Frontend (Vercel) and backend (Azure) each independently re-verify eligibility before deploying.
- Code scanning is owned by GitHub CodeQL **Default setup** — do not add CodeQL jobs to CI.

## Conventions & gotchas cheat-sheet

- **Read the relevant `AGENTS.md` first** (root + scoped). It governs; this file orients.
- **Source-of-truth order:** current code/config/migrations/tests → runtime contracts (OpenAPI) → accepted ADRs + maintained docs → Linear → dated plans (history only).
- **Don't hand-edit generated output** (Orval client, generated contracts). Change the source and regenerate.
- **Tenant isolation & authorization are backend responsibilities.** Frontend guards are UX only.
- **Danish UI copy** and Danish numeric input (`NumericInput`, decimal comma).
- **Prefer editing one maintained doc** over creating a competing one; state facts as facts, decisions as decisions, plans as plans (see [`Docs/AGENTS.md`](Docs/AGENTS.md)).
- Apply the **Customer-value gate** ([`Docs/agents/CUSTOMER_VALUE_GATE.md`](Docs/agents/CUSTOMER_VALUE_GATE.md)) before building new customer-facing features; it never overrides security/compliance/tenant-isolation fixes.
- Personal-data / external-processor / AI changes: consult [`Docs/compliance/GDPR_AI_ACT_BASELINE.md`](Docs/compliance/GDPR_AI_ACT_BASELINE.md).

## Where to look next

| Need | Start here |
|---|---|
| Agent rules (governing) | [`AGENTS.md`](AGENTS.md) + scoped: [`src/FE`](src/FE/AGENTS.md), [`src/BE/WorkslipApi`](src/BE/WorkslipApi/AGENTS.md), [`src/BE/infrastructure`](src/BE/infrastructure/AGENTS.md), [`Docs`](Docs/AGENTS.md) |
| Docs index & truth model | [`Docs/README.md`](Docs/README.md) |
| Architecture & ADRs | [`Docs/architecture/README.md`](Docs/architecture/README.md) |
| Domain / tenancy | [`Docs/architecture/domain-and-dataflows.md`](Docs/architecture/domain-and-dataflows.md) |
| API contract & integration | [`Docs/api/README.md`](Docs/api/README.md) |
| Validation policy | [`Docs/agents/VALIDATION.md`](Docs/agents/VALIDATION.md) |
| Delivery handoffs / review gates | [`Docs/agents/DELIVERY_HANDOFFS.md`](Docs/agents/DELIVERY_HANDOFFS.md) |
| CI / release / production boundary | [`Docs/operations/ci-quality-gates.md`](Docs/operations/ci-quality-gates.md) |
| Local dev (full detail) | [`Docs/operations/local-development.md`](Docs/operations/local-development.md) |
| Frontend / Backend / Infra setup | [`src/FE/README.md`](src/FE/README.md) · [`src/BE/WorkslipApi/README.md`](src/BE/WorkslipApi/README.md) · [`src/BE/infrastructure/README.md`](src/BE/infrastructure/README.md) |
