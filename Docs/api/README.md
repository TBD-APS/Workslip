# API and integration documentation

**State:** Draft  
**Owner:** API owner (assign in Linear)  
**Review cadence:** Every API contract, authentication or integration change.

The runtime OpenAPI document and endpoint implementation are the primary sources for implemented API behaviour. The Postman collection is executable integration evidence when it is run against an isolated non-production environment.

Until WOR-146 is completed:

- inspect `src/BE/WorkslipApi/Endpoints/` for current routes;
- inspect `ResultExtensions.ToHttpResult` for the common HTTP error mapping;
- use the runtime OpenAPI endpoint and Scalar UI only in an approved non-production environment;
- run `src/BE/WorkslipApi/Postman/run-integration-tests.sh` only against localhost, test or staging;
- do not infer endpoint availability from dated plans.

This area will contain the maintained authentication, permissions, pagination/filtering, error-contract, correlation-ID, idempotency, retry and compatibility guidance.
