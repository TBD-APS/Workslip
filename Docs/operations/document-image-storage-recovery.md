# Document and image blob storage recovery

Status: Active operations runbook  
Owner: Workslip  
Source of truth: `.github/workflows/infrastructure-production-reconcile.yml`, `.github/workflows/backend-production-deploy.yml`, `src/BE/infrastructure/main.bicep`, `src/BE/infrastructure/dynamicConfig.bicep`, `src/BE/infrastructure/staticConfig.bicep` and the storage adapters under `src/BE/WorkslipApi/Workslip.Infrastructure/Storage`  
Review cadence: whenever the blob storage account, its containers, the API managed-identity role assignment or the `Azure:DocumentFileStorage:*` configuration changes  
Linear: RBJ-335

## Symptom

The document (Docs) and image features fail in an environment even though the API is otherwise healthy:

- `GET /api/jobs/{jobId}/images` (the job image list) returns **500**. This is the visible failure; the frontend global error toast for this call was suppressed, but the underlying server error remains.
- Document attachment and image **upload** calls fail.
- Single-item reads (`GET .../images/{imageId}`, `GET .../profile-image`, document attachment download) return **404** rather than 500, so they degrade quietly.
- `JobDeletionCleanupService` logs `Scheduled job deletion cleanup failed` because it cannot reach blob storage while purging deleted jobs.
- `GET /health` still returns `ok`, so the automatic production deploy reports success and the app stays up.

The asymmetry between the list endpoint (500) and single-item reads (404) is expected: the single-item adapters catch `RequestFailedException` with status 404 and return `null`, while the list and bulk-delete adapters enumerate blobs without that guard, so any storage failure surfaces as an unhandled 500. See `AzureBlobImageStorage.cs` and `AzureBlobDocumentAttachmentStorage.cs`.

## Scope

Apply this runbook only after Diagnosis shows a storage resource is actually missing. The same symptoms — the image-list 500 included — can also come from an application or credential fault while the storage account, `uploads` container, role assignment and `Azure:DocumentFileStorage:*` configuration are all present and correct. When every piece in Diagnosis is present, this runbook does not apply: read the unhandled-exception type logged by `GlobalExceptionMiddleware` in Application Insights (it records `ExceptionType`, `traceId` and `correlationId`) and treat it as an application error instead.

## Root cause

Application code and Azure infrastructure are released through two independent tracks:

| Track | Trigger | What it applies |
|---|---|---|
| [`backend-production-deploy.yml`](../../.github/workflows/backend-production-deploy.yml) | Automatic current-production deploy on every green `CI` run on `main`; manual allowlisted new-tenant deploy during cutover | The API application package and reviewed database migrations only. It never runs Bicep and only reads existing app settings. |
| [`infrastructure-production-reconcile.yml`](../../.github/workflows/infrastructure-production-reconcile.yml) | Manual (`workflow_dispatch`) from `main` | [`deploy-infrastructure.ps1`](../../src/BE/infrastructure/deploy-infrastructure.ps1) → [`main.bicep`](../../src/BE/infrastructure/main.bicep): the storage account, blob containers, managed-identity role assignments and the App Configuration key values. |

The document/image feature stores files in Azure Blob Storage using the API managed identity. In any non-Development environment the concrete adapters require infrastructure that only the reconcile track provisions:

- App Configuration key `Azure:DocumentFileStorage:StorageAccountName` — written by [`dynamicConfig.bicep`](../../src/BE/infrastructure/dynamicConfig.bicep). Without it the adapter throws `InvalidOperationException` in its constructor on first use.
- The storage account and the `uploads` blob container — created by [`main.bicep`](../../src/BE/infrastructure/main.bicep); the container name is set to `uploads` by [`staticConfig.bicep`](../../src/BE/infrastructure/staticConfig.bicep).
- The `Storage Blob Data Contributor` role for the API managed identity on the storage account — assigned by [`main.bicep`](../../src/BE/infrastructure/main.bicep).

When the feature code reaches production ahead of a matching reconcile, the code is live but the blob resources it depends on are missing, so blob operations fail. The gap is often **partial** rather than all-or-nothing: the storage account and the `Azure:DocumentFileStorage:StorageAccountName` value are shared with Power BI worksheet export (`PowerBiWorksheetExportStorage` reads the same key) and can already exist from an earlier reconcile, while the pieces this feature adds — the `uploads` container and the `Storage Blob Data Contributor` assignment for the API managed identity — remain absent until the environment is reconciled again. The code never creates the container itself; it depends on the reconciled infrastructure.

Because the account and the account-name key can be present while the container or role assignment are not, confirm each piece independently (see Diagnosis) instead of assuming the whole storage stack is present or absent as a unit.

Note: `src/BE/infrastructure/staticConfig.json` is a stale generated ARM artifact (it still names a `report-attachments` container) and is not referenced by any workflow or script. The authoritative container value is `uploads` in `staticConfig.bicep`.

## Diagnosis

Confirm which piece is missing before reconciling. Replace the resource names if the target environment is not production (`prod`). Production names derive from company `mrsoftware` and environment `prod`.

```bash
# Missing App Configuration key? KeyNotFound confirms the configuration gap.
az appconfig kv show --name appcs-mrsoftware-prod \
  --key 'Azure:DocumentFileStorage:StorageAccountName' --auth-mode login

# Does the storage account exist?
az storage account show -g rg-mrsoftware-prod -n stmrsoftwareprod -o none

# Does the uploads container exist? ResourceNotFound means the container is missing.
az storage container-rm show -g rg-mrsoftware-prod \
  --storage-account stmrsoftwareprod -n uploads -o none

# Which container is the API configured to use? It must match a container that exists.
az appconfig kv show --name appcs-mrsoftware-prod \
  --key 'Azure:DocumentFileStorage:ContainerName' --auth-mode login --query value -o tsv

# Does the API managed identity hold Storage Blob Data Contributor on the account?
apiPrincipalId=$(az identity show -g rg-mrsoftware-prod -n id-mrsoftware-prod --query principalId -o tsv)
accountId=$(az storage account show -g rg-mrsoftware-prod -n stmrsoftwareprod --query id -o tsv)
az role assignment list --assignee-object-id "$apiPrincipalId" --scope "$accountId" \
  --role 'Storage Blob Data Contributor' --query '[0].id' -o tsv
```

Read the exception type behind the 500 in Application Insights (see [`APPLICATION_INSIGHTS_ERROR_DASHBOARD.md`](APPLICATION_INSIGHTS_ERROR_DASHBOARD.md)) to pinpoint the missing piece:

- `InvalidOperationException` referencing `Azure:DocumentFileStorage:StorageAccountName` — the configuration key is missing.
- `RequestFailedException` status `403` — the managed-identity role assignment is missing or has not propagated.
- `RequestFailedException` status `404` with `ContainerNotFound` — the request reached and authenticated against the account, but the **configured** container does not exist. Either the container was never created, or `Azure:DocumentFileStorage:ContainerName` names a container that was never provisioned — for example a stale `report-attachments` value while the provisioned container is `uploads`. A 404 here proves the account name and the role assignment are already correct, so the container name is the only remaining variable.

## Recovery

1. Ensure `main` is green and carries the intended infrastructure definition.
2. Run **Infrastructure · Production reconcile** ([`infrastructure-production-reconcile.yml`](../../.github/workflows/infrastructure-production-reconcile.yml)) from `main` via `workflow_dispatch`: first select `plan` / `current-production` with `PLAN CURRENT PRODUCTION`, then review the preview. With explicit approval, select `reconcile` / `current-production`, type `RECONCILE CURRENT PRODUCTION`, and approve the `prod` environment. It runs `deploy-infrastructure.ps1` for `mrsoftware` / `prod`, which deploys `main.bicep` in incremental mode: it creates the storage account and `uploads` container, assigns `Storage Blob Data Contributor` to the API managed identity, and writes the `Azure:DocumentFileStorage:*` App Configuration values. The workflow restarts the current API at the end so the singleton blob clients pick up the new configuration.
3. If you reconcile outside that workflow, restart the API afterwards so it re-reads App Configuration:

   ```bash
   az webapp restart -g rg-mrsoftware-prod -n api-mrsoftware-prod
   ```

4. If Diagnosis shows only the container name is wrong — a `404 ContainerNotFound` while the `uploads` container exists — the reconcile realigns `Azure:DocumentFileStorage:ContainerName` from `staticConfig.bicep`. For immediate relief without a full reconcile, set the value directly and restart so the app re-reads App Configuration:

   ```bash
   az appconfig kv set --name appcs-mrsoftware-prod \
     --key 'Azure:DocumentFileStorage:ContainerName' --value uploads --auth-mode login --yes
   az webapp restart -g rg-mrsoftware-prod -n api-mrsoftware-prod
   ```

Role-assignment propagation can lag; if reads still return `403` immediately after reconcile, wait and retry before assuming a further fault.

## Verification

- `az appconfig kv show ... --key 'Azure:DocumentFileStorage:StorageAccountName'` returns the storage account name.
- The storage account exists and contains the `uploads` container.
- The API managed identity holds `Storage Blob Data Contributor` on the account.
- `GET /api/jobs/{jobId}/images` returns `200` with an array (empty for a job with no images) instead of `500`.
- An image upload, download and delete round-trip succeeds for a test job.
- A document attachment upload and download round-trip succeeds.
- No new `Scheduled job deletion cleanup failed` entries appear after the restart.

## Prevention

Treat storage-backed features as having an infrastructure prerequisite. When a change adds or renames a storage account, container, role assignment or `Azure:DocumentFileStorage:*` key, run the infrastructure reconcile for the target environment before — or together with — the release that depends on it, rather than relying on the automatic backend deploy. The infrastructure entry points and their sequence are documented in [`../../src/BE/infrastructure/README.md`](../../src/BE/infrastructure/README.md); release-track expectations are in [`ci-quality-gates.md`](ci-quality-gates.md).

Keep the container name in `staticConfig.bicep` in agreement with the container that `main.bicep` actually creates. A drift between the two — or a deployed `Azure:DocumentFileStorage:ContainerName` value left behind by an older reconcile — produces the `ContainerNotFound` failure above even though the account, container and role assignment all exist.
