# Branch-matched frontend validation

**Status:** Active  
**Owner:** Workslip maintainers  
**Applies to:** Pull requests that change the frontend, backend API contract, or the shared API-generation workflow

## Purpose

Frontend validation must generate the Orval client from the backend OpenAPI contract in the same pull-request merge result. It must not depend on generated files from another branch, an anonymously exposed production OpenAPI endpoint, or production credentials.

The shared action is `.github/actions/generate-frontend-api/action.yml`. The pull-request workflow is `.github/workflows/frontend-validation.yml`.

## Contract-generation startup mode

The action sets the environment variable:

```text
Workslip__GenerateOpenApiOnly=true
```

ASP.NET Core maps this to `Workslip:GenerateOpenApiOnly`.

In this mode the API must still:

- register application and infrastructure services needed for endpoint discovery;
- configure middleware and endpoint mappings;
- generate the OpenAPI document from the current backend commit.

In this mode the API must not:

- resolve `SqlDbContext` for startup validation;
- initialize or alter the database schema;
- test database connectivity;
- seed release-testing data;
- start database-backed hosted services or notification workers.

Normal application startup does not set this flag and retains fail-fast SQL configuration, schema initialization, connectivity validation, optional release-testing seeding, and all hosted services.

## Validation sequence

The frontend pull-request workflow performs these steps from a clean checkout:

1. install frontend dependencies;
2. test the lint-debt comparator;
3. capture ESLint JSON for the pull-request checkout;
4. create a clean worktree for the pull-request base branch, install that branch's frontend dependencies and capture its ESLint JSON;
5. fail if the pull request introduces any new severity-2 ESLint error compared with the base branch;
6. restore the backend API project and run the focused startup/authorization regression suite through the shared API-generation action;
7. build the API in Release mode and generate the OpenAPI document;
8. generate the Orval client from that document;
9. run Vitest;
10. run the production frontend build, including application and service-worker type checking.

The lint gate is deliberately a ratchet while inherited lint debt exists. Existing errors do not make every pull request red, but new errors are blocking. Warnings remain informational. The comparator fingerprints the file, rule, message and offending source rather than the line number so unrelated line movement does not turn old debt into a false new error.

Tests and production build still run after successful contract generation. A failing no-new-lint comparison, unit test, contract generation or build fails the workflow.

## Security boundary

Do not add a production SQL connection string, Azure SQL access, Key Vault secret, or production App Configuration access to pull-request validation merely to generate OpenAPI.

A pull request must never be able to run schema initialization or database-backed workers against production as a side effect of contract inspection. Endpoint metadata generation is a build-time concern and must remain isolated from runtime infrastructure side effects.

## Regression requirements

Changes to API startup, infrastructure registration, hosted services, OpenAPI generation, or the shared frontend validation action must preserve tests proving that:

- contract-generation mode does not resolve database services;
- contract-generation mode registers no Workslip hosted services;
- normal runtime still requires database services;
- normal runtime still registers the expected hosted services;
- an OpenAPI document and Orval client are generated without SQL configuration.

Changes to the lint ratchet must preserve focused tests proving that existing findings remain allowed while genuinely new errors and additional occurrences are rejected.

## Troubleshooting

If generation fails with `Missing SQL connection string`, verify that the OpenAPI step sets `Workslip__GenerateOpenApiOnly=true` and that no new startup path resolves database services before endpoint discovery.

If SQL retry or worker logs appear during OpenAPI generation, review all `IHostedService` registrations. Contract-generation mode must omit background services even when direct schema initialization is already skipped.

If the lint ratchet reports a new error, fix that error in the pull request. Do not add it to a static allow-list or disable the rule to preserve the inherited baseline.

If Orval succeeds but later gates fail, treat unit-test discovery and TypeScript/build failures as their own verified defects. Do not weaken those gates or reintroduce production credentials to hide unrelated frontend baseline problems.
