# Power BI worksheet report

**Status:** Implementation ready; production activation requires the privacy/processor gate, a published report and a suitable Power BI/Fabric sharing license

**Owner:** Workslip product owner and operations

**Linear:** WOR-451, WOR-542

## Outcome

Workslip produces a minimized worksheet snapshot in a private Azure Blob container. Power BI reads that single blob with the report owner's Microsoft organizational account. No Workslip bearer token, storage key, SAS token, SQL credential or on-premises gateway is stored in Power BI.

```text
Workslip SQL
  -> Workslip API managed identity
  -> private identity-bound Power BI container/worksheets.csv
  -> Power BI import model
  -> named report viewers
  -> authenticated Power BI report embedded in Workslip Admin Timer
```

The export is disabled by default. Enabling it is an explicit production operation because worksheet, employee and customer data is copied to another processing surface.

## Exported schema

The snapshot intentionally excludes organization IDs, user IDs, job IDs and customer addresses.

| Column | Type | Purpose |
|---|---|---|
| `WorkDate` | Date | Date analysis |
| `Year`, `Month`, `IsoWeek` | Whole number | Stable grouping without locale-dependent text |
| `ReportNumber` | Text | Job/report drill-down label |
| `CustomerName` | Text | Customer grouping |
| `Employee` | Text | Employee grouping |
| `JobType` | Text | Work category |
| `HoursWorked` | Decimal | Hours measure |
| `HasOutlay` | Boolean | Outlay count/filter |
| `BillableHourlyRate`, `BillableAmount` | Decimal | Billing measures |
| `ExportedAtUtc` | Date/time/timezone | Freshness indicator |

The worker rewrites one complete UTF-8 CSV snapshot atomically every 60 minutes and covers the most recent 24 months. App Service failures are logged without email addresses, organization IDs or worksheet content.

## Production activation gate

Before passing `-EnablePowerBiExport`, record in WOR-451:

1. controller/processor roles and the approved purpose;
2. the Microsoft DPA/subprocessor and EEA-region assessment;
3. retention (currently a rolling 24-month export) and deletion/tenant-exit handling;
4. the named access group or users and quarterly access review owner;
5. DPIA screening result;
6. confirmation that customer name, employee name, hours and billing values are necessary.

Do not activate with production data until the accountable owner has approved those points.

## Azure activation

Use the existing deployment entry point. Supply values at deployment time; do not commit a UPN or Entra object ID to source control.

```powershell
.\deploy.ps1 prod `
  -PowerBiReaderPrincipalId '<ENTRA-OBJECT-ID>' `
  -PowerBiReaderEmail '<POWER-BI-ACCOUNT-UPN>' `
  -EnablePowerBiExport
```

This creates a private container whose name is derived from the Entra reader identity, grants only **Storage Blob Data Reader** on that container to that user, writes the non-secret runtime configuration to App Configuration and starts the exporter after the next API deployment. The runtime matches both the UPN and Entra object ID to exactly one Workslip Admin and verifies that the identity-bound container name still matches; zero, multiple, non-Admin or drifted matches stop the export.

## Power Query

Create a parameter named `BlobUrl` with the deployment output `POWER_BI_WORKSHEETS_BLOB_URL`. Then create a query named `Worksheets`:

```powerquery
let
    Source = AzureStorage.BlobContents(BlobUrl),
    Csv = Csv.Document(
        Source,
        [
            Delimiter = ",",
            Columns = 13,
            Encoding = 65001,
            QuoteStyle = QuoteStyle.Csv
        ]
    ),
    Headers = Table.PromoteHeaders(Csv, [PromoteAllScalars = true]),
    Types = Table.TransformColumnTypes(
        Headers,
        {
            {"WorkDate", type date},
            {"Year", Int64.Type},
            {"Month", Int64.Type},
            {"IsoWeek", Int64.Type},
            {"ReportNumber", type text},
            {"CustomerName", type text},
            {"Employee", type text},
            {"JobType", type text},
            {"HoursWorked", type number},
            {"HasOutlay", type logical},
            {"BillableHourlyRate", Currency.Type},
            {"BillableAmount", Currency.Type},
            {"ExportedAtUtc", type datetimezone}
        },
        "en-US"
    )
in
    Types
```

Select **Organizational account** when Power Query asks for credentials. Microsoft documents Azure Blob Storage as supporting Organizational account authentication in Power BI semantic models and dataflows. `AzureStorage.BlobContents` returns the binary content of the exact blob URL, which avoids listing unrelated storage objects.

## Semantic model

Use one import-mode fact table named `Worksheets`. Disable auto date/time and create a date table when Power BI Desktop is available:

```dax
Date =
CALENDAR ( MIN ( Worksheets[WorkDate] ), MAX ( Worksheets[WorkDate] ) )
```

Relate `Date[Date]` (one) to `Worksheets[WorkDate]` (many), single direction. Mark `Date` as the date table.

Recommended measures:

```dax
Total Hours = SUM ( Worksheets[HoursWorked] )

Billable Amount = SUM ( Worksheets[BillableAmount] )

Outlay Entries =
CALCULATE ( COUNTROWS ( Worksheets ), Worksheets[HasOutlay] = TRUE () )

Average Hours Per Day =
DIVIDE ( [Total Hours], DISTINCTCOUNT ( Worksheets[WorkDate] ) )

Last Exported At = MAX ( Worksheets[ExportedAtUtc] )
```

The first report page should contain:

- KPI cards for total hours, billable amount, outlay entries and last export time;
- line chart: total hours by date;
- clustered bar chart: total hours by employee;
- matrix: customer -> report number with hours and billable amount;
- slicers: date, employee, customer and job type.

## Refresh and sharing

Use Import mode and schedule Power BI refresh after the Workslip export cadence, for example 06:15, 12:15 and 18:15 Europe/Copenhagen. Azure Blob is a cloud source, so no on-premises gateway is required when the service can reach the storage endpoint. Enable refresh-failure notifications and verify the first scheduled refresh in refresh history.

Share the report only with named people or an approved Entra security group. Do not grant Build or Reshare unless required. A hidden visual or column is not a security boundary; Power BI report access also grants access to the underlying semantic model.

For **embed for your organization**, each viewer normally needs the appropriate Power BI Pro/PPU entitlement unless the content is hosted on qualifying Fabric/Premium capacity that permits Free viewers. License/capacity must therefore be settled before treating the embedded report as a shared production feature.

## Workslip report link and embedded analytics

After the report is published, store its normal authenticated Power BI Service report URL in the runtime configuration key `PowerBiReport:Url`. The value is not a secret. Use a normal `https://app.powerbi.com/groups/.../reports/<report-guid>/...` URL; never use `Publish to web` (`/view?r=...`).

`GET /api/worksheets/all/report/power-bi` requires the Workslip Admin policy and sends `Cache-Control: no-store`. It validates the Power BI host, HTTPS/default port, report/workspace/tenant identifiers and supported report path before returning:

```json
{
  "url": "https://app.powerbi.com/groups/.../reports/...",
  "embedUrl": "https://app.powerbi.com/reportEmbed?reportId=...&autoAuth=true..."
}
```

The embed URL is derived server-side from the approved report URL. Workslip does not receive, store or forward a Power BI access token. The Admin Timer page renders the report in an authenticated Microsoft Power BI iframe and keeps **Åbn i Power BI** as a fallback. Microsoft/Entra authentication, report permissions, Power BI licensing and any report RLS remain Power BI security boundaries.

The frontend query cache includes the current Workslip organization ID so report configuration is not reused across organization-session changes. Non-admin users do not render the admin reporting component, and the API remains the authoritative Admin boundary.

Removing `PowerBiReport:Url` is the application-level rollback: the iframe and fallback link disappear without affecting worksheet capture or the WOR-451 export worker.

## Local validation for WOR-542

Use the canonical Windows development bootstrap. Do not copy production credentials or enable the production export merely to test the UI.

### No-configuration state

```powershell
git fetch origin
git switch --track origin/rbj--542-power-bi-embedded-analytics
.\dev.ps1
```

Open `http://127.0.0.1:5270/app/timer` as the synthetic local Admin. Confirm that **Power BI-overblik** is visible and shows **Power BI er ikke konfigureret endnu**. A normal synthetic user must not receive the Admin reporting view.

### Embedded-report state

If an authenticated non-production Power BI report is already available, set only its normal report URL in the backend process environment before starting the stack:

```powershell
$env:PowerBiReport__Url = 'https://app.powerbi.com/groups/me/reports/<REPORT-GUID>/ReportSection'
.\dev.ps1
```

Then open `http://127.0.0.1:5270/app/timer` as the local Admin and verify:

1. **Power BI-overblik** renders an iframe inside Workslip;
2. Microsoft prompts for organizational sign-in inside the Power BI flow if the browser has no suitable Microsoft session;
3. the intended report renders after sign-in for an authorized viewer;
4. report slicers/filters can be used without leaving Workslip;
5. **Åbn i Power BI** opens the same authenticated report as fallback;
6. refresh/retry states do not expose tokens or report data in Workslip errors;
7. a narrow/mobile viewport remains usable.

Clear the temporary local variable after testing:

```powershell
Remove-Item Env:PowerBiReport__Url -ErrorAction SilentlyContinue
```

Local synthetic Workslip authentication can prove Workslip UI and API behavior, but it cannot prove a real user's Power BI license, workspace permission, RLS, Microsoft tenant configuration or scheduled refresh. Those remain live integration gates.

Current Microsoft documentation:

- Azure Blob connector and organizational authentication: https://learn.microsoft.com/en-us/power-query/connectors/azure-blob-storage
- `AzureStorage.BlobContents`: https://learn.microsoft.com/en-us/powerquery-m/azurestorage-blobcontents
- Scheduled refresh and shared-capacity limits: https://learn.microsoft.com/en-us/power-bi/connect-data/refresh-data
- Report sharing and license/security requirements: https://learn.microsoft.com/en-us/power-bi/collaborate-share/service-share-dashboards
- Secure portal/website report embedding: https://learn.microsoft.com/en-us/power-bi/collaborate-share/service-embed-secure
- Embedded analytics capacity/licensing: https://learn.microsoft.com/en-us/power-bi/developer/embedded/embedded-capacity

## Verification

1. Confirm `PowerBiExport:Enabled` is `true` only after approval.
2. Confirm the container has no public access and the report owner has reader access only at container scope.
3. Confirm `worksheets.csv` exists and its metadata contains `schema-version=1` and a recent `exported-at-utc`.
4. Confirm the header has exactly 13 columns and excludes address and GUID fields.
5. Sign in to the Azure Blob connector with the intended organizational account and run **Refresh now**.
6. Confirm KPI totals against Workslip for one synthetic or approved test month.
7. Confirm a non-Admin cannot read `/api/worksheets/all/report/power-bi` and an unauthorized Entra viewer cannot open the report.
8. Set `PowerBiReport:Url` to a published authenticated report URL and confirm the report is embedded for an Admin on `/app/timer`.
9. Confirm the embedded report and fallback link both require the intended Microsoft identity and report permission; verify a negative cross-tenant/RLS case before production sharing.
10. Confirm filters/slicers function inside Workslip and the iframe remains usable at desktop and narrow viewport widths.
11. Confirm three scheduled refreshes complete without interactive login and that refresh failure notification is configured.
12. Remove `PowerBiReport:Url` and confirm the embedded report and fallback link disappear without affecting worksheet operations.

The previously shared Workslip bearer token must never be used in this setup. Log out of old Workslip sessions and sign in again to invalidate/replace that session material.
