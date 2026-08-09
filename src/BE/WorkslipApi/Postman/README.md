# Workslip Postman integration tests

**Status:** Active  
**Owner:** API owner  
**Source of truth:** endpoint code and runtime OpenAPI; `postman_collection.json` is executable integration coverage  
**Review cadence:** on public API-contract or authentication changes

Purpose: execute the active `Workslip.Api` contract assertions against an isolated non-production API. JSON parsing alone is not integration evidence; the collection is run with Newman and assertion/request failures fail CI.

## Contract ownership

Use these together:

1. endpoint and contract source under `src/BE/WorkslipApi`;
2. runtime `/openapi/v1.json` for the exact running build when enabled;
3. `postman_collection.json` for executable examples, pre-request scripts and assertions.

Endpoint registrations/runtime OpenAPI remain authoritative. Postman must follow that contract; it does not define a competing route model.

The old `/api/dev/*` authentication/debug endpoints have been removed from the application. The runner explicitly excludes the retained top-level `Dev` folder from success-path execution and verifies that it contains only `/api/dev/*` requests, so stale development-only expectations cannot silently enter the active integration run. New active API folders are included automatically.

## Environment contract

The full suite mutates data and exercises integrations. Run it only against localhost or a dedicated integration/staging deployment, never customer production data.

Required runtime values:

- `WORKSLIP_INTEGRATION_BASE_URL`: isolated API base URL, for example `https://<integration-app>.azurewebsites.net`;
- `WORKSLIP_AUTH_TOKEN`: pre-issued bearer token for the isolated test identity. It must have the permissions required by the collection.

No tokens or environment-specific credentials belong in the collection or Postman environment file. GitHub CI receives them from repository/automation secrets.

The runner refuses URLs that do not look like localhost/test/staging unless `ALLOW_PRODUCTION_INTEGRATION_TESTS=true` is deliberately supplied. That override is not approved for normal CI.

## Test data strategy

The collection generates unique per-run organization, CVR, user, report and customer values where supported. The integration environment must use synthetic/non-production data. Serialize full-suite runs when they share one environment so mutation-heavy tests do not race each other.

## Local or controlled automation run

```bash
cd src/BE/WorkslipApi/Postman
WORKSLIP_AUTH_TOKEN=<test-token> \
  ./run-integration-tests.sh https://<integration-api-base-url>
```

Equivalent with environment variables:

```bash
WORKSLIP_INTEGRATION_BASE_URL=https://<integration-api-base-url> \
WORKSLIP_AUTH_TOKEN=<test-token> \
  ./run-integration-tests.sh
```

## CI run

The unified `.github/workflows/frontend-validation.yml` workflow contains a blocking **Postman integration** job. It:

1. requires `WORKSLIP_INTEGRATION_BASE_URL` and `WORKSLIP_AUTH_TOKEN` automation secrets;
2. serializes Postman integration executions against the shared isolated target;
3. runs `run-integration-tests.sh` with Newman;
4. feeds the job result into `CI Gate`.

Missing integration configuration, Newman request failures and `pm.test` assertion failures are blocking failures; the job does not silently skip.

## Coverage expectation

For public contract changes, update the matching Postman request/assertions when executable integration behavior changes. Cover success plus material validation, permission, not-found, conflict, retry/idempotency and tenant-isolation behavior where the suite is the appropriate boundary.

A collection description or green JSON parser is not evidence that runtime security or behavior exists. Source, runtime OpenAPI, focused backend tests and the executed Newman result remain distinct evidence layers.
