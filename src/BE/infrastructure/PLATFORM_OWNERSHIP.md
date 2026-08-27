# Platform infrastructure ownership

Shared Azure infrastructure for Workslip is owned by MR SAAS'y.

Canonical source:

`TBD-APS/mr-saassy/infrastructure/workloads/workslip/azure`

The baseline was imported from Workslip `main` commit `31aa38ebc689f1de030c6ca83351317c40af6ea1` with exact source blob hashes recorded in the SAAS'y `source-manifest.json`.

The Bicep files that remain in this repository are a compatibility copy for currently-running product workflows. They are not the source of truth for future Azure topology changes and must not diverge independently.

Workslip continues to own database schema/migrations, application code, tests/build artifacts and product-specific deployment hooks. MR SAAS'y owns shared Azure resource topology, hosting, managed identities, OIDC/RBAC, Key Vault/App Configuration topology, monitoring and cost controls.
