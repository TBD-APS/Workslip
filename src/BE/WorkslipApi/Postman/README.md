# Workslip Postman integration tests

**Status:** Active  
**Owner:** API owner  
**Source of truth:** endpoint code and runtime OpenAPI; `postman_collection.json` is executable integration coverage  
**Review cadence:** on public API-contract or authentication changes

Purpose: execute the active `Workslip.Api` contract assertions against an isolated non-production API. Parsing the collection is source validation only; runtime evidence requires Newman to execute the requests and `pm.test` assertions.

## Contract ownership

Use these together:

1. endpoint and contract source under `src/BE/WorkslipApi`;
2. runtime `/openapi/v1.json` for the exact running build when API reference endpoints are enabled;
3. `postman_collection.json` for executable examples, pre-request scripts and assertions;
4. `Docs/api` for shared semantics, compatibility policy and integration guidance.

Do not maintain or depend on a separate hand-written endpoint catalog. Endpoint registrations and runtime OpenAPI own the route inventory.

Development/release-test exposure is fail-closed in `ConfigureDevEnvironment`: `/api/dev/*` is mapped only in ASP.NET Development, while OpenAPI/Scalar are additionally controlled by the resolved release-testing policy. The hosted CI flow relies on that Development-only boundary for a synthetic local bearer token; it never enables the endpoint in staging or production.

## GitHub-hosted CI

For every non-documentation CI run, the `Postman integration (ephemeral)` job creates an isolated runtime on a GitHub-hosted Ubuntu runner:

1. generate a random SQL Server password and JWT signing key for the run;
2. start SQL Server 2022 Developer in Docker;
3. start `Workslip.Api` in ASP.NET Development against the ephemeral database;
4. apply local migrations and seed synthetic development data;
5. obtain a bearer token for the seeded synthetic admin through `/api/dev/token`;
6. run `run-integration-tests.sh` with Newman against `http://127.0.0.1:5262`;
7. fail `CI Gate` on request/assertion/runtime failure;
8. stop the API and force-remove the SQL container through an `EXIT` trap, including failure/cancellation paths that allow shell cleanup to run.

No persistent integration API, database or GitHub bearer-token secret is required. The random database/JWT credentials and bearer token exist only inside the runner and are masked in GitHub Actions output.

The root `docker-compose.yml` remains the local Seq development service. CI does not extend that compose contract merely to host one isolated test dependency; it uses the same ephemeral SQL-container pattern as Workslip's existing local Playwright workflow.

The canonical hosted runner entrypoint is:

```bash
bash src/BE/WorkslipApi/Postman/run-hosted-integration.sh
```

It requires Docker, .NET, Node.js, `curl` and `openssl`, all supplied by the GitHub-hosted job after its setup steps.

## Controlled external/local run

`run-integration-tests.sh` can still execute the collection against an explicitly supplied localhost or isolated staging target:

```bash
cd src/BE/WorkslipApi/Postman
WORKSLIP_AUTH_TOKEN=<synthetic-test-token> \
  ./run-integration-tests.sh https://<isolated-staging-api-base-url>
```

Equivalent with environment variables:

```bash
WORKSLIP_INTEGRATION_BASE_URL=https://<isolated-staging-api-base-url> \
WORKSLIP_AUTH_TOKEN=<synthetic-test-token> \
  ./run-integration-tests.sh
```

The runner refuses URLs that do not look like localhost/test/staging unless `ALLOW_PRODUCTION_INTEGRATION_TESTS=true` is explicitly set. That override is not approved for normal CI. Production data and production bearer tokens must never be used.

## Test data strategy

The hosted CI runtime starts with a fresh SQL Server database and synthetic development seed for each job. Collection variables generate unique values where supported. This removes cross-PR mutation races and means full-suite runs do not need serialization against a shared environment.

A request that invokes a real external integration is still a real integration boundary. Do not introduce mocks merely to make the full collection green. If a boundary cannot safely run in the ephemeral job, classify it explicitly and give it its own appropriate isolated evidence rather than silently skipping it.

## Coverage expectation

For every public contract change, update the matching Postman request/assertions when executable integration behaviour changes. Cover success plus material validation, permission, not-found, conflict, retry/idempotency and tenant-isolation behaviour where Postman is the appropriate boundary.

A collection description, JSON parse or syntax check is not runtime API evidence. Source, runtime OpenAPI, focused backend tests and the executed Newman result are distinct evidence layers.
