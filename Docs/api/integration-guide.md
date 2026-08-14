# Integration guide

**Status:** Active  
**Owner:** Backend/API  
**Source of truth:** runtime OpenAPI, endpoint code and executable Postman evidence  
**Review cadence:** On authentication or API-contract changes

This page is for running and diagnosing integrations. Shared HTTP semantics belong in [`contract.md`](contract.md).

## Choose an environment

Use localhost or an isolated release-test/staging environment with synthetic data. Do not run destructive integration suites against live customer production.

The repository release policy controls published API reference tooling outside local development. `/api/dev/token` is mapped only in ASP.NET Development; it is not a release-test or integration authentication mechanism.

## Authenticate

Use the authentication flow appropriate to the integration/runtime contract. Current browser/user flows include Microsoft/Entra login and Workslip token exchange. Local development may expose development-only shortcuts; an isolated staging environment must use its approved normal test-authentication path.

For repeatable isolated API testing, a pre-issued token can be supplied to the Postman runner through its supported environment variable rather than embedding credentials in files or commands committed to the repository.

## Run the executable suite

```bash
cd src/BE/WorkslipApi/Postman
./run-integration-tests.sh https://<test-or-staging-api>
```

With a pre-issued token, use the runner's documented `WORKSLIP_AUTH_TOKEN` input.

There is no general production mutation-test workflow. Run the suite deliberately against the approved isolated target.

## Request conventions

For authenticated JSON requests, use the headers defined in [`contract.md`](contract.md), including correlation and idempotency headers where the endpoint contract requires them.

Example read:

```bash
curl --fail-with-body \
  -H 'Accept: application/json' \
  -H 'Authorization: Bearer <token>' \
  -H 'X-Correlation-ID: <correlation-id>' \
  'https://<test-api>/api/jobs?status=Draft&limit=25&offset=0'
```

For an idempotent mutation, use one stable idempotency key for one logical request. Retry the same logical request with the same key only when the original result is unknown; use a new key when the content changes.

## Failure handling

- `400` — correct request/validation errors.
- `401` — re-authenticate; do not retry indefinitely.
- `403` — authenticated identity lacks permission.
- `404` — treat as unavailable in the caller's scope; do not infer cross-tenant ownership.
- `409` — inspect the stable error code and resolve the business/idempotency conflict.
- `428` — supply the required idempotency key.
- `429` — back off according to the endpoint/client policy.
- `500` — preserve correlation context; retry only when the operation is known to be retry-safe.

For cache-enabled reads, retain the returned ETag and revalidate with `If-None-Match`; a `304` means reuse the cached representation.

## Verification checklist

1. Confirm the route/shape in the OpenAPI document for the exact running build.
2. Confirm authorization and tenant scope in endpoint/service source.
3. Exercise the relevant Postman request or equivalent HTTP scenario.
4. Cover failure paths that matter to the changed risk.
5. Verify two-tenant behaviour when tenant-owned data is involved.
6. Preserve correlation/idempotency behaviour across retries and support diagnostics.
7. Record any difference between the running contract and maintained guidance as documentation drift.
