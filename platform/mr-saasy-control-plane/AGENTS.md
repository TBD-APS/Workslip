# MR SAAS'y control-plane agent rules

Root [`AGENTS.md`](../../AGENTS.md) applies. These rules are stricter for the isolated Laravel control plane.

## Trust boundary

This service is an MR SAAS'y platform component, not a Workslip domain module.

- Do not reference Workslip backend/frontend/domain projects, DTOs, repositories, schemas or database credentials.
- Product data may enter only through explicit `ProductAdapters/Contracts` after policy/minimization work permits it.
- `app/AI/Application`, `app/AI/Agents` and `app/AI/Providers` must never use Laravel DB facade, Eloquent models/builders, query builder, raw database clients such as PDO/mysqli, database connection configuration/credentials, or platform persistence implementations.
- Provider adapters may depend on provider contracts and narrow HTTP transport only; they must not resolve generic platform secrets directly.
- Provider/model selection must never expand the caller's data permissions.
- Unknown tenant/agent/capability state fails closed.
- New application classes/dependencies must be assigned to an explicit Deptrac layer; uncovered dependencies fail the architecture gate rather than silently bypassing the ruleset.

## Gate 0

Before provider implementations are added, all of these must remain green:

```bash
composer validate --strict
php artisan test
vendor/bin/deptrac analyse --config-file=deptrac.yaml --fail-on-uncovered
php scripts/forbid-ai-db-symbols.php
php scripts/assert-forbidden-deptrac-fixtures.php
php scripts/assert-uncovered-deptrac-fixture.php
```

Do not add a Deptrac baseline or suppression to hide a new AI/provider dependency violation.

## Delivery

- Keep this directory independently bootable and extractable.
- No AI provider credential or Workslip database credential is required for Gate 0.
- Keep generated architecture evidence under `build/` and out of source control.
- Any later repository extraction must preserve these namespaces/contracts and trust directions rather than introducing compatibility coupling back to Workslip.
