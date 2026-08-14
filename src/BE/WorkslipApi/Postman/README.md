# Workslip Postman integration tests

**Status:** Active  
**Owner:** API owner  
**Source of truth:** endpoint code, runtime OpenAPI and `postman_collection.json`  
**Review cadence:** on public API-contract or authentication changes

Purpose: run the active `Workslip.Api` contract against a non-production API before and after contract changes.

## Contract sources

Use these together:

1. endpoint and contract source under `src/BE/WorkslipApi`;
2. runtime `/openapi/v1.json` for the exact deployed build when API reference endpoints are enabled;
3. `postman_collection.json` for executable examples and assertions;
4. `Docs/api` for shared semantics, compatibility policy and integration guidance.

Do not maintain or depend on a separate hand-written endpoint catalog. Endpoint registrations and runtime OpenAPI own the route inventory.

Development/release-test exposure is fail-closed in `ConfigureDevEnvironment`: `/api/dev/*` is mapped only in ASP.NET Development, while OpenAPI/Scalar are additionally controlled by the resolved release-testing policy. Do not use development endpoints as production integration authentication.

## Environment contract

Use localhost or a dedicated integration/staging deployment, never production data.

Required runtime variable:

- `baseUrl`: API base URL, for example `https://<staging-app>.azurewebsites.net`.

Optional runtime variable:

- `WORKSLIP_AUTH_TOKEN`: bearer token for protected endpoints when the collection cannot obtain one through an approved test flow. The runner passes it as `authToken`.

No secrets belong in the collection or Postman environment file. Store environment-specific values in the local shell or an approved isolated automation secret store.

## Test data strategy

The collection generates unique per-run values for organization, CVR, users, report numbers and customers where supported. Choose one documented strategy:

1. Reset an isolated integration database before the suite.
2. Keep it persistent and rely on unique per-run data for smoke runs.

Production data must never be used.

## Local or controlled automation run

```bash
cd src/BE/WorkslipApi/Postman
./run-integration-tests.sh https://<staging-api-base-url>
```

Equivalent with an environment variable:

```bash
WORKSLIP_INTEGRATION_BASE_URL=https://<staging-api-base-url> \
  ./run-integration-tests.sh
```

With a pre-issued test token:

```bash
WORKSLIP_AUTH_TOKEN=<token> \
  ./run-integration-tests.sh https://<staging-api-base-url>
```

The runner refuses URLs that do not look like localhost/test/staging unless `ALLOW_PRODUCTION_INTEGRATION_TESTS=true` is explicitly set. That override is not approved for normal use.

There is currently no merged GitHub Actions workflow that executes Newman against an isolated integration target. Any such automation must use a dedicated isolated environment and must be introduced as a reviewed workflow rather than relying on unmerged or historical configuration.

## Coverage expectation

For every public contract change, update the matching Postman request and test success plus relevant validation, permission, not-found, conflict, retry/idempotency and tenant-isolation behaviour. A collection description is not evidence that runtime security or behaviour exists; source and deployed OpenAPI remain authoritative.
