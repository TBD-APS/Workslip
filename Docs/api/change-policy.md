# API contract change policy

**Status:** Active  
**Owner:** Backend/API

A change has API-contract impact when it changes a route/method, authorization policy, request/response model, enum, validation/error code, query semantics, required header, idempotency/retry behaviour, caching/correlation behaviour or OpenAPI generation.

## Required review

For an API-impacting change:

1. update endpoint/contract source and response metadata;
2. verify the runtime/generated OpenAPI representation;
3. regenerate/review the frontend client when affected;
4. update Postman examples/assertions when they are used as executable integration evidence;
5. update only the maintained `Docs/api` guidance whose durable semantics changed;
6. call out externally visible compatibility impact in the PR/release notes when relevant.

Do **not** maintain a second hand-written route catalog. Endpoint registrations and runtime OpenAPI own that fact.

## Compatibility

| Class | Examples | Treatment |
|---|---|---|
| Additive | new optional field or endpoint | normal review + relevant examples/tests |
| Behavioural | new validation, permission or default | explicit callout + regression tests |
| Breaking | removed/renamed field, route, enum or stable error code | migration/deprecation plan |
| Security | auth boundary, tenant scope, token handling | security review + negative tests |

A deprecated external contract should record its replacement, known consumers, removal condition/date and owner. Do not remove a route merely because the repository frontend no longer uses it; external consumers may exist.

If a required generated or executable integration artifact cannot be synchronized in the same PR, record the exact gap, reason, owner and follow-up issue. Do not describe the contract as current until the missing artifact has been reconciled.
