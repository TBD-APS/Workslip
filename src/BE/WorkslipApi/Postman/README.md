# Workslip Postman integration tests

Purpose: run the active `Workslip.Api` contract against a non-production deployed API before/after backend changes.

## Environment contract

Use a dedicated integration/staging deployment, not production.

Required runtime variable:

- `baseUrl`: base URL for the deployed Workslip API, for example `https://<staging-app>.azurewebsites.net`.

Optional runtime variable:

- `WORKSLIP_AUTH_TOKEN`: bearer token used for protected endpoints when the target environment does not allow the collection to obtain one via `POST /api/auth/verify-code`. The runner passes it as Postman `authToken`; leave it unset locally if the auth folder can capture a token itself.

No secrets belong in the Postman environment file. Store deploy-specific values as GitHub Secrets/Variables or local shell environment variables.

## Test data strategy

The collection generates unique per-run values for:

- CVR
- organization name
- admin email
- report number
- customer email

That makes repeated runs reproducible on a persistent test database and avoids the old fixed `12345678` CVR collision. The test database still must be isolated from production. Reset strategy is one of:

1. Drop/recreate the test database before a release validation run, then let `WorkslipSchemaRunner` bootstrap schema/taxonomy on API startup.
2. Keep the database persistent and rely on unique per-run test data for normal smoke runs.

Production data must never be used for these tests. Grim, obvious, still worth spelling out.

## Local/manual run

```bash
cd src/BE/WorkslipApi/Postman
./run-integration-tests.sh https://<staging-api-base-url>
```

Equivalent without argv:

```bash
WORKSLIP_INTEGRATION_BASE_URL=https://<staging-api-base-url> ./run-integration-tests.sh
```

With a pre-issued test bearer token:

```bash
WORKSLIP_AUTH_TOKEN=<token> ./run-integration-tests.sh https://<staging-api-base-url>
```

The runner refuses URLs that do not look like localhost/test/staging unless `ALLOW_PRODUCTION_INTEGRATION_TESTS=true` is explicitly set.

## CI run

GitHub Actions workflow: `.github/workflows/integration-tests.yml`.

Configure one of:

- Repository secret `WORKSLIP_INTEGRATION_BASE_URL`; or
- manual workflow input `base_url`.

Optional secret:

- `WORKSLIP_AUTH_TOKEN` when the staging environment requires a pre-issued bearer token for protected endpoints.

Then run workflow `Workslip Integration Tests`.
