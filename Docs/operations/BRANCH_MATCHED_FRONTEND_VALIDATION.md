# Branch-matched frontend validation

**Status:** Active  
**Owner:** Workslip maintainers  
**Applies to:** Unified pull-request and `main` CI

## Purpose

Frontend validation must generate the Orval client from the backend OpenAPI contract in the same revision. It must not depend on generated files from another branch, an anonymously exposed production OpenAPI endpoint, or production credentials.

The shared action is `.github/actions/generate-frontend-api/action.yml`. The unified workflow is `.github/workflows/frontend-validation.yml`, displayed in GitHub Actions as `CI`.

## Contract-generation startup mode

The action sets:

```text
Workslip__GenerateOpenApiOnly=true
```

ASP.NET Core maps this to `Workslip:GenerateOpenApiOnly`.

In this mode the API must still register the services required for endpoint discovery, configure endpoint mappings and generate the OpenAPI document from the current backend revision.

It must not resolve production database services, alter schema, seed data or start database-backed workers merely to inspect the API contract.

Normal application startup does not set this flag and retains its runtime infrastructure checks and hosted services.

## CI ownership

The shared API-generation action owns only:

1. .NET setup and API restore;
2. isolated Release-mode OpenAPI generation;
3. Orval generation from that document.

Backend correctness tests are deliberately not hidden inside the action. The unified `Backend` CI job runs the full backend Release build and test suite once.

The `Frontend + API contract` job owns:

1. frontend dependency installation;
2. lint-ratchet regression tests;
3. current ESLint inventory;
4. exact baseline ESLint inventory;
5. rejection of new severity-2 lint findings;
6. branch-matched API client generation;
7. Vitest; and
8. the production frontend build, including application and service-worker type checking.

On pull requests the lint baseline is the exact pull-request base SHA. On a `main` push it is the previous `main` SHA, so a bypassed or unexpected regression cannot silently grow the lint baseline.

## Security boundary

Do not add a production SQL connection string, Azure SQL access, Key Vault secret or production App Configuration access to CI merely to generate OpenAPI.

A pull request must never be able to initialize production schema or start database-backed workers as a side effect of contract inspection. Endpoint metadata generation remains an isolated build-time concern.

## Regression requirements

Changes to API startup, infrastructure registration, hosted services, OpenAPI generation or the shared action must preserve the contract-generation isolation boundary.

Changes to the lint ratchet must preserve focused tests proving that inherited findings remain allowed while genuinely new errors and additional occurrences are rejected.

The full backend suite in the `Backend` job is the regression owner for startup, authorization, tenant isolation and other backend behavior; do not duplicate a hand-picked subset inside the contract generator.

## Troubleshooting

If generation fails with `Missing SQL connection string`, verify that the OpenAPI step sets `Workslip__GenerateOpenApiOnly=true` and that no startup path resolves database services before endpoint discovery.

If SQL retry or worker logs appear during OpenAPI generation, review hosted-service registration. Contract-generation mode must omit background services.

If the lint ratchet reports a new error, fix that error in the pull request. Do not add it to a static allow-list or disable the rule to preserve inherited debt.

If Orval succeeds but unit tests or the TypeScript/build gate fails, treat that as its own defect. Do not weaken the gate or add production credentials to hide it.
