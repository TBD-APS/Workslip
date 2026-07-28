# Integration guide

**Status:** Active  
**Owner:** API owner  
**Source of truth:** runtime OpenAPI, endpoint code and the maintained Postman collection  
**Review cadence:** on authentication or API-contract changes

## 1. Choose an environment

Use localhost or a dedicated integration/staging deployment with isolated test data. Do not run mutation smoke tests against production.

The Postman runner rejects URLs that do not look like localhost/test/staging unless `ALLOW_PRODUCTION_INTEGRATION_TESTS=true` is explicitly set. Do not use that override in normal work.

## 2. Obtain a token

Supported flows are:

- local email code: `POST /api/auth/send-code`, then `POST /api/auth/verify-code/{code}`
- Microsoft/Entra login: present an Entra JWT to `POST /api/auth/entra-login`
- invitation enrollment: present an Entra JWT to `POST /api/auth/entra-enroll`
- pre-issued integration token through `WORKSLIP_AUTH_TOKEN`

The `/api/dev/token` shortcut is not a production integration mechanism. Its current production exposure is tracked by WOR-182 and must not be relied on.

## 3. Run the executable contract

```bash
cd src/BE/WorkslipApi/Postman
./run-integration-tests.sh https://<staging-api-base-url>
```

With a pre-issued token:

```bash
WORKSLIP_AUTH_TOKEN=<token> \
  ./run-integration-tests.sh https://<staging-api-base-url>
```

There is no active GitHub Actions integration-test workflow. Run this suite manually or from explicitly approved isolated-environment automation.

## 4. Make an authenticated request

```bash
curl --fail-with-body \
  -H 'Accept: application/json' \
  -H 'Authorization: Bearer <token>' \
  -H 'X-Correlation-ID: 6aabf9ef-b307-4c88-a07d-f405ec30d65a' \
  'https://<staging-api>/api/jobs?status=Draft&limit=25&offset=0'
```

Example paginated response:

```json
{
  "items": [],
  "totalCount": 0
}
```

## 5. Make an idempotent mutation

Use one stable key for one logical request. Generate a new key for a different payload.

```bash
curl --fail-with-body \
  -X POST \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json' \
  -H 'Authorization: Bearer <token>' \
  -H 'X-Correlation-ID: 9a8e50ac-9645-4558-8eb0-d25493e64fb8' \
  -H 'Idempotency-Key: 9a8e50ac-9645-4558-8eb0-d25493e64fb8' \
  --data @create-job.json \
  'https://<staging-api>/api/jobs/'
```

Retry the identical request with the same key only when the first result is unknown. Do not reuse the key for edited content.

## 6. Handle failures

- `400`: map field errors back to the request.
- `401`: refresh or re-authenticate; do not retry forever.
- `403`: identity is valid but lacks permission.
- `404`: do not infer whether another organization owns the resource.
- `409`: inspect `error`; resolve the business conflict or idempotency misuse.
- `428`: add the required `Idempotency-Key`.
- `429`: respect throttling and back off.
- `500`: preserve the correlation ID and report it; retry only when the operation is known to be safe.

## 7. Cache-aware reads

Store the `ETag` returned by cache-enabled GET endpoints and revalidate with `If-None-Match`. Treat `304` as “use the cached representation”; it has no response body.

## 8. Contract verification checklist

1. Confirm the route exists in the running OpenAPI document.
2. Confirm the authorization policy in endpoint source.
3. Use or add its Postman request and assertions.
4. Test success, validation, authorization, not-found and conflict paths where applicable.
5. Verify organization isolation with two test tenants for tenant-owned data.
6. Preserve correlation and idempotency headers.
7. Record any contract difference before release.
