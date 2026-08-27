# Workslip infrastructure instructions

Root [`../../../AGENTS.md`](../../../AGENTS.md) applies. These rules cover `src/BE/infrastructure/` and related deployment wiring.

## Platform ownership

MR SAAS'y is the authoritative owner of shared Azure/cloud topology. The canonical Workslip Azure baseline lives in `TBD-APS/mr-saassy/infrastructure/workloads/workslip/azure`.

Files that remain in this product repository are compatibility and product-operation material only. Do not evolve App Service plans/SKUs, SQL topology, Storage, App Configuration, Key Vault, managed identities, GitHub OIDC/RBAC, ACS, monitoring or Azure budgets here. Make those changes in `mr-saassy` first. Product-owned schema/migrations, product artifacts and product-specific deployment hooks stay here.

This ownership boundary does not require extra deployment gates and must not be used to reintroduce redundant approval/validation layers.

## Boundaries

- Keep provisioning separate from application runtime behaviour.
- Keep each permission, role assignment, secret owner and configuration key authoritative in one place.
- Do not add competing deployment scripts or duplicate permission sets.
- Preserve stable resource names unless a migration explicitly requires change.
- Runtime services must not depend on deployment-only administrator credentials.

## Safe change process

Before changing stateful infrastructure, identify resource recreation/mutation, OIDC assumptions, managed identities, Key Vault references, rollback and recovery.

Do not perform live deployment, resource deletion, role removal, DNS change, migration or secret rotation unless that operation is explicitly in scope and approved.

## Azure/identity review

Review least privilege, Graph permissions, managed-identity ownership, OIDC subject/environment matching, secret lifecycle, SQL admin/runtime separation, idempotent membership/role assignment, duplicate Entra registrations, redirect URIs/origins and environment isolation.

## Scripts and CI/CD

Scripts must fail on external-command errors, clean temporary files, avoid ambiguous flags, be rerunnable without duplicate state, never print secrets and return actionable failures.

Keep workflows purpose-specific. Use GitHub OIDC rather than publish profiles/stored Azure credentials. Do not duplicate validation or add privileged automation without a concrete need.

## Validation delta

Follow [`../../../Docs/agents/VALIDATION.md`](../../../Docs/agents/VALIDATION.md). Infrastructure changes normally require the validation that protects the changed surface; deployment and runtime smoke are separate evidence and only run when explicitly in scope. Do not add duplicate gates solely because ownership moved repositories.
