# Infrastructure deployment

## Entra application registrations

Entra application registrations are provisioned separately from Azure infrastructure.

Run the Entra deployment only when registration settings change or when setting up an environment:

```powershell
.\deploy-entra.ps1 prod
```

The command reconciles these stable alternate keys:

- `workslip-oauth-server-{environment}`
- `workslip-client-{environment}`

It writes resolved application and service-principal IDs to the ignored local state file:

```text
entra.{environment}.local.json
```

The signed-in Azure CLI user must be allowed to manage application registrations and service principals. Graph write failures are reported immediately with the original Graph diagnostic instead of being retried as if they were replication delays.

The script preserves existing IDs for managed roles and scopes. It also preserves unmanaged legacy roles and scopes as writable projections, avoiding accidental deletion of active entitlements while the registration is reconciled.

## Azure infrastructure only

Deploy or debug Azure infrastructure without modifying the registrations:

```powershell
.\deploy-infrastructure.ps1 prod
```

The infrastructure command uses the local state file when it exists. When the file is missing, it performs read-only discovery in this order:

1. Read the existing OAuth and client IDs from Azure App Configuration and resolve their application object IDs through Entra.
2. Look up both applications through their stable Microsoft Graph `uniqueName` values.
3. Fail with an instruction to run `deploy-entra.ps1` only when neither source can resolve both registrations.

Successful discovery is cached in the ignored `entra.{environment}.local.json` file. The command validates the environment and tenant, creates a temporary Bicep handoff file, invokes the existing infrastructure deployment, and restores the committed placeholder afterward. Read-only discovery does not update app registrations or service principals.

## Full deployment

To run both phases in order:

```powershell
.\deploy-safe.ps1 prod
```

This is equivalent to running `deploy-entra.ps1` followed by `deploy-infrastructure.ps1`.

The committed `entra-provisioned.json` file is only a placeholder required by Bicep compilation. Deployment scripts restore it after infrastructure deployment. Do not place real tenant IDs in that file.
