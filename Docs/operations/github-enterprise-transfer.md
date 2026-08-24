# GitHub organization transfer cutover

Workslip uses GitHub OIDC subjects that include the immutable GitHub owner ID and repository ID. A repository transfer changes the owner login and owner ID while the repository ID remains stable.

## Before transfer

1. Merge the transfer-hardening PR and ensure CI is green.
2. Confirm repository ID is still `1245555609`.
3. Confirm both production boundaries exist:
   - `rg-mrsoftware-prod`
   - `rg-mrsoftwarev2-live`
4. Do not run a production deployment during the transfer window.

## Immediately after transfer

Authenticate `gh` against the new organization and `az` against the Workslip Azure subscription, then run:

```powershell
./src/BE/infrastructure/reconcile-github-oidc-after-transfer.ps1 -WhatIf
```

Verify the printed subjects use the new organization login and numeric owner ID, while the repository ID remains `1245555609`.

Then reconcile the credentials:

```powershell
./src/BE/infrastructure/reconcile-github-oidc-after-transfer.ps1
```

The script updates both the GitHub deployment identity and the database migration identity for `prod` and `live` using the repository metadata returned by GitHub. It does not guess the new organization ID.

## Verification

After OIDC reconciliation:

1. Install/authorize required GitHub Apps for the new organization.
2. Verify repository environments and secrets are present.
3. Run normal PR CI on a no-op infrastructure/docs PR.
4. Verify Azure login succeeds from the protected `prod` environment without deploying application code.
5. Verify the migration identity can obtain an Azure token without applying a migration.
6. Enable the repository-level merge queue ruleset only after the above checks pass.

Do not rely on old `rasm105k/Workslip-v2.0` URLs as policy identifiers after transfer. GitHub redirects are convenient for humans but should not be used as authorization or deployment trust.
