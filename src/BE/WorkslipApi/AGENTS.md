# Workslip backend/API instructions

Root [`../../../AGENTS.md`](../../../AGENTS.md) applies. These rules cover `src/BE/WorkslipApi/`.

## Layer boundaries

- Keep endpoints thin and business/workflow rules in application or domain code.
- Keep EF Core, SQL, Graph, email, storage and other integration details in infrastructure.
- Do not expose persistence rows as API contracts.
- Reuse established repositories, services, validators and mappings before introducing new abstractions.

## Ardalis.Result

Application services return `Result<T>` or `Result`. Endpoints map them through `ResultExtensions.ToHttpResult`.

Do not create competing result wrappers or duplicate generic result-to-HTTP mapping inside endpoints. Endpoint-owned mapping is for presentation concerns only.

## Authorization and tenant isolation

For affected operations, verify server-side policy, repository/query tenant scope, role changes/revocation, Superadmin semantics and cache/log isolation. Never derive tenant authority from untrusted client headers or frontend state.

## EF Core and failure boundaries

When relevant, review SQL translation, bounded queries, indexes/N+1, transaction scope, retry execution strategies, concurrency/lost updates, idempotency, deletion/orphans and existing production data before constraints.

Use relational tests for behaviour that depends on SQL translation, constraints, transactions or concurrency; EF in-memory behaviour is not evidence for those cases.

For Graph/Entra/email/storage side effects, define both partial-failure directions, retries, compensation and concurrent duplicate requests before choosing the mechanism.

## API contracts

Endpoint source and runtime OpenAPI define the contract. Keep maintained API docs and generated clients/Postman material consistent with contract changes; do not hand-edit generated output.

## Validation delta

Follow [`../../../Docs/agents/VALIDATION.md`](../../../Docs/agents/VALIDATION.md). Backend runtime changes require a Release build plus focused tests; authorization/API/persistence changes require the corresponding HTTP or relational validation described there.
