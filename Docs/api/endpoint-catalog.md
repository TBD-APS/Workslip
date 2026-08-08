# Endpoint catalog (superseded)

**Status:** Historical / superseded  
**Superseded by:** endpoint registrations and runtime OpenAPI

The hand-maintained endpoint catalog was retired because it duplicated the API source and repeatedly drifted from the running implementation.

For the current route set:

1. inspect `src/BE/WorkslipApi/Endpoints/`;
2. use the runtime OpenAPI document for the running build when enabled in the target environment;
3. use `src/BE/WorkslipApi/Postman/` for executable examples and verification.

Durable API semantics that are not expressed by OpenAPI remain documented in [`contract.md`](contract.md), [`change-policy.md`](change-policy.md) and [`integration-guide.md`](integration-guide.md).
