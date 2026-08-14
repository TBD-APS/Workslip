# API and integration documentation

**Status:** Active  
**Owner:** Backend/API owner  
**Source of truth:** endpoint registrations, runtime OpenAPI and executable integration evidence  
**Review cadence:** On API contract, authentication or integration changes

## Where to look

- [`contract.md`](contract.md) — shared HTTP/auth/error/idempotency contract.
- [`change-policy.md`](change-policy.md) — compatibility and API-change rules.
- [`integration-guide.md`](integration-guide.md) — integration usage and operational expectations.
- `src/BE/WorkslipApi/Endpoints/` — current route registrations.
- runtime OpenAPI — generated contract for the running build when enabled for the target environment.
- `src/BE/WorkslipApi/Postman/` — executable verification/examples, not a competing contract source.

## Documentation policy

Do not maintain a second hand-written list of every route. It duplicates endpoint registrations/OpenAPI and drifts quickly.

Document only behaviour that is not obvious from the generated contract: authorization semantics, tenant boundaries, compatibility rules, error conventions, idempotency/retry expectations and integration failure behaviour.

When a contract changes, update the endpoint source first, regenerate dependent clients/contracts through the established process, then update the maintained guidance that explains the change.
