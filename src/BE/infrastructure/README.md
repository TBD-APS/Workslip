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

After Entra has been provisioned successfully, deploy or debug Azure infrastructure without touching the registrations:

```powershell
.\deploy-infrastructure.ps1 prod
```

This command reads the local Entra state, validates the environment and tenant, creates a temporary Bicep handoff file, and invokes the existing infrastructure deployment.

## Full deployment

To run both phases in order:

```powershell
.\deploy-safe.ps1 prod
```

This is equivalent to running `deploy-entra.ps1` followed by `deploy-infrastructure.ps1`.

The committed `entra-provisioned.json` file is only a placeholder required by Bicep compilation. Deployment scripts restore it after infrastructure deployment. Do not place real tenant IDs in that file.
