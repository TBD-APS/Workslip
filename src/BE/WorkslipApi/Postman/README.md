# Workslip Postman integration tests

Purpose: run the active `Workslip.Api` contract against a non-production API before and after contract changes.

## Contract sources

Use these together:

1. endpoint and contract source under `src/BE/WorkslipApi`
2. runtime `/openapi/v1.json` for the exact deployed build
3. `postman_collection.json` for executable examples and assertions
4. `Docs/api` for conventions, endpoint catalog and integration guidance

The application currently maps OpenAPI, Scalar and `/api/dev/*` through `ConfigureDevEnvironment`, but the environment guard is commented out. Do not assume dev endpoints are restricted merely because the collection labels them development-only. Use them only against an isolated local environment.

## Environment contract

Use a dedicated integration/staging deployment, not production.

Required runtime variable:

- `baseUrl`: base URL for the deployed API, for example `https://<staging-app>.azurewebsites.net`.

Optional runtime variable:

- `WORKSLIP_AUTH_TOKEN`: bearer token for protected endpoints when the collection cannot obtain one through an approved test authentication flow. The runner passes it as `authToken`.

No secrets belong in the collection or Postman environment file. Store deploy-specific values as GitHub Secrets/Variables or local shell environment variables.

## Test data strategy

The collection generates unique per-run values for organization, CVR, users, report numbers and customers where supported. This reduces collisions on a persistent isolated test database.

Choose one documented strategy:

1. Reset an isolated integration database before the suite.
2. Keep it persistent and rely on unique per-run data for smoke runs.

Production data must never be used.

## Local/manual run

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

## CI run

Workflow: `.github/workflows/integration-tests.yml`.

Configure one of:

- repository secret `WORKSLIP_INTEGRATION_BASE_URL`
- manual workflow input `base_url`

Optional secret:

- `WORKSLIP_AUTH_TOKEN`

Then run **Workslip Integration Tests**.

## Coverage expectation

For every public contract change, update the matching Postman request and test success plus relevant validation, permission, not-found, conflict, retry/idempotency and tenant-isolation behaviour. A collection description is not evidence that runtime security or behaviour exists; source and deployed OpenAPI remain authoritative.
