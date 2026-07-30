# Workslip infrastructure instructions

Read the root `AGENTS.md`, `Docs/agents/OPERATING_CONTRACT.md`, and `Docs/agents/VALIDATION.md` before changing infrastructure.

## Scope

These rules apply to `src/BE/infrastructure/`, deployment scripts, Bicep, Entra registrations, Key Vault integration, Azure SQL provisioning, App Service configuration, and related CI/CD wiring.

## Boundaries and source of truth

- Keep infrastructure provisioning separate from application runtime behavior.
- Keep Entra application registration deployment separate from ordinary Azure resource deployment where the established architecture requires it.
- Declare each permission, role assignment, secret owner, and configuration key in one authoritative place.
- Do not add competing deployment scripts or duplicate permission sets.
- Preserve stable resource naming unless a migration plan explicitly requires a rename.
- Do not store generated credentials, passwords, client secrets, signing keys, connection strings, or private keys in source control or logs.

## Safe change process

Before editing:

- inspect the current resource graph and deployment scripts;
- identify whether the change recreates or mutates an existing production resource;
- identify stateful resources and irreversible effects;
- inspect GitHub environment/OIDC assumptions;
- inspect Key Vault references and managed identities;
- identify rollback and recovery behavior.

Do not perform a live deployment, migration, resource deletion, role removal, DNS change, or secret rotation unless deployment is explicitly in scope and approved.

## Azure and identity review

Review as applicable:

- least-privilege RBAC and Microsoft Graph permissions;
- user-assigned versus system-assigned managed identity ownership;
- OIDC subject and environment matching;
- Key Vault access and secret lifecycle;
- SQL administrator and runtime identity separation;
- idempotent role/group membership provisioning;
- duplicate app registrations and enterprise applications;
- redirect URIs, origins, and environment-specific domains;
- production versus development configuration isolation;
- sensitive output masking.

Application runtime must not depend on deployment-only administrator credentials.

## Scripts

PowerShell and shell scripts must:

- fail on non-zero external command exit codes;
- clean temporary files in `finally` blocks;
- avoid ambiguous CLI flags;
- avoid self-modifying workflow behavior;
- be rerunnable without duplicating memberships, role assignments, or resources;
- never print secret values;
- provide actionable errors that identify the failed resource or operation.

## CI/CD

- Keep workflows minimal and purpose-specific.
- Do not duplicate validation already performed reliably elsewhere.
- Do not remove a check without understanding the risk it covers.
- Use GitHub OIDC rather than publish profiles or stored Azure credentials.
- Keep deployment and validation workflows separate when that improves diagnosis.
- Do not let unrelated workflow noise block small cohesive PRs.

## Required validation

For infrastructure changes, run as applicable:

- Bicep build/lint;
- PowerShell or shell syntax validation;
- Azure `what-if` or equivalent plan output;
- focused script tests or dry runs;
- workflow syntax validation;
- post-deployment health and authentication smoke tests when deployment is explicitly in scope.

A successful template build does not prove a deployment will succeed. A successful deployment does not prove login, SQL access, secrets, DNS, or application behavior work; verify the affected runtime path separately.
