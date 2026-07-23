# API contract change policy

## Required review

A PR has API impact when it changes any of:

- route, HTTP method or authorization policy
- request/response model or enum
- validation rule or error code
- pagination, filtering or sorting
- required header, idempotency or retry behaviour
- cache, ETag or correlation behaviour
- OpenAPI generation or Postman examples

The PR must then update or explicitly confirm:

1. endpoint source and `Produces` metadata
2. generated OpenAPI output
3. frontend generated client where affected
4. Postman request and assertions
5. `Docs/api` catalog/guide
6. release notes for externally visible changes

## Compatibility classes

| Class | Examples | Release treatment |
|---|---|---|
| Additive | New optional field, new endpoint | Normal review; add examples/tests |
| Behavioural | New validation, permission or default | Call out explicitly; regression tests required |
| Breaking | Removed/renamed field, route, enum or error contract | Migration/deprecation plan required |
| Security | Auth boundary, tenant scope, token handling | Security review and negative tests required |

## Deprecation

A deprecated contract must document:

- replacement
- first deprecated release/date
- known consumers
- removal condition/date
- owner

Do not silently remove a route because the frontend no longer uses it; integrations may exist outside the repository.

## Waiver

A release may temporarily proceed without a synchronized documentation artifact only when the PR records:

- missing artifact
- reason
- owner
- follow-up issue
- expiry date

The waiver is evidence of known debt, not proof that the contract is current.
