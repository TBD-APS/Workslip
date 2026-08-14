# Workslip API contract

**Status:** Active  
**Owner:** Backend/API  
**Source of truth:** endpoint registrations and runtime OpenAPI  
**Review cadence:** With every endpoint, authentication or response-contract change

This page documents cross-cutting contract rules that are expensive to infer from generated OpenAPI. It intentionally does not repeat the full route catalog.

## Runtime contract

Use these sources in order:

1. endpoint/contract source under `src/BE/WorkslipApi`;
2. runtime OpenAPI for the exact running build when the target environment enables it;
3. `Postman/postman_collection.json` for executable examples and smoke assertions;
4. this page for shared semantics.

`/api/dev/*` is an ASP.NET Development-only surface. OpenAPI and Scalar are controlled separately by the resolved release-testing policy and may therefore be available outside Development only while that policy explicitly enables them. Do not assume any of these reference/test surfaces exist in every environment.

## Authentication and tenant authority

Send accepted Workslip/Entra bearer tokens as:

```http
Authorization: Bearer <token>
```

The API derives user, role and effective organization from authenticated server context. Integrations must not treat a client-selected organization ID, frontend route state or UI guard as an authorization boundary.

Policy intent:

| Policy | Meaning |
|---|---|
| Anonymous | No bearer token required |
| `RequireReadAccess` | Authenticated read-capable role |
| `RequireUser` | Operational user or configured higher role |
| `RequireAdmin` | Admin or configured higher role |
| `RequireSuperAdmin` | Superadmin only |

Superadmin cross-tenant behaviour is valid only on explicitly Superadmin-scoped platform operations. Ordinary repositories/services remain tenant-scoped to the effective organization.

## Standard request headers

```http
Accept: application/json
Content-Type: application/json
X-Correlation-ID: <uuid-or-trace-id>
Idempotency-Key: <stable-key-for-one-logical-mutation>
```

`Idempotency-Key` applies where the endpoint contract requires it. A retry of the same logical mutation should reuse the same key; using the same key for different content is a conflict.

## Result and error mapping

Application services return `Ardalis.Result`; endpoints normally map through `ResultExtensions.ToHttpResult`.

| Result | HTTP |
|---|---:|
| Success | `200` |
| No content | `204` |
| Invalid | `400` validation problem |
| Unauthorized | `401` |
| Forbidden | `403` |
| Not found | `404` |
| Conflict | `409` |
| Missing required idempotency key | `428` |
| Unexpected failure | `500` |

Integrations should branch primarily on HTTP status and use a stable `error` code where the endpoint returns one. Do not depend on human-readable error text as a machine contract.

## Pagination, filtering and arrays

List endpoints use their OpenAPI-defined query parameters. Common pagination parameters are `limit` and `offset`; common sort parameters are `sortBy` and `sortDirection`.

Repeated values are serialized as repeated query parameters when the contract defines an array, for example:

```text
/api/jobs?status=Draft&status=InReview&limit=50&offset=0
```

Paginated responses normally expose `items` plus `totalCount` when that shape is defined by the endpoint contract.

## Caching and correlation

Selected GET endpoints use private ETag revalidation. Clients may send `If-None-Match` and must handle `304 Not Modified` when the endpoint advertises that behaviour.

Preserve `X-Correlation-ID` across integration boundaries when supplied and include correlation identifiers in support/diagnostic reports. Do not put personal data or secrets into correlation values.

## Compatibility

- Additive optional response fields are normally backward-compatible.
- Removing/renaming routes, fields, enum values or stable error codes requires a migration/deprecation decision.
- Authentication/tenant-boundary changes are security-sensitive even when the JSON shape is unchanged.
- Generated frontend clients and executable Postman examples must be reviewed/regenerated when their source contract changes.
- Planned behaviour must never be written here as deployed behaviour.

Use [`change-policy.md`](change-policy.md) for change/deprecation rules and [`integration-guide.md`](integration-guide.md) for integration operation/failure guidance.
