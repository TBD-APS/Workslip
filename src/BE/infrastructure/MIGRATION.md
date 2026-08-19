# Minimal Azure migration

Use only these two files for the temporary tenant rebuild path:

- `deploy-migration.ps1`
- `migration.bicep`

Example:

```powershell
./deploy-migration.ps1 `
  -CompanyName "mrsoftware" `
  -Environment "live" `
  -Location "norwayeast" `
  -ExpectedTenantId "d700dfea-febb-4673-8587-fa4e57c66ad1" `
  -ExpectedSubscriptionId "672b29f5-7993-482a-963a-078d2be58bdc"
```

SQL is skipped by default. Add `-DeploySql` only when SQL provisioning is allowed, and set `WORKSLIP_SQL_ADMIN_PASSWORD` first.

Use `-WhatIf` only after the resource group exists.
