# Power BI worksheet report

**Status:** Implementation ready; production activation requires the privacy/processor gate and a permanent Power BI sharing license

**Owner:** Workslip product owner and operations

**Linear:** WOR-451

## Outcome

Workslip produces a minimized worksheet snapshot in a private Azure Blob container. Power BI reads that single blob with the report owner's Microsoft organizational account. No Workslip bearer token, storage key, SAS token, SQL credential or on-premises gateway is stored in Power BI.

```text
Workslip SQL
  -> Workslip API managed identity
  -> private identity-bound Power BI container/worksheets.csv
  -> Power BI import model
  -> named report viewers
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

This creates a private container whose name is derived from the Entra reader identity, grants only **Storage Blob Data Reader** on that container to that user, writes the non-secret runtime configuration to App Configuration and starts the exporter after the next API deployment. The runtime matches both the UPN and Entra object ID to exactly one Workslip organization and verifies that the identity-bound container name still matches; zero, multiple or drifted matches stop the export.

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

Current Microsoft documentation:

- Azure Blob connector and organizational authentication: https://learn.microsoft.com/en-us/power-query/connectors/azure-blob-storage
- `AzureStorage.BlobContents`: https://learn.microsoft.com/en-us/powerquery-m/azurestorage-blobcontents
- Scheduled refresh and shared-capacity limits: https://learn.microsoft.com/en-us/power-bi/connect-data/refresh-data
- Report sharing and license/security requirements: https://learn.microsoft.com/en-us/power-bi/collaborate-share/service-share-dashboards

## Verification

1. Confirm `PowerBiExport:Enabled` is `true` only after approval.
2. Confirm the container has no public access and the report owner has reader access only at container scope.
3. Confirm `worksheets.csv` exists and its metadata contains `schema-version=1` and a recent `exported-at-utc`.
4. Confirm the header has exactly 13 columns and excludes address and GUID fields.
5. Sign in to the Azure Blob connector with the intended organizational account and run **Refresh now**.
6. Confirm KPI totals against Workslip for one synthetic or approved test month.
7. Confirm an unauthorized Entra user cannot open the blob or report.
8. Confirm the copied report link works in a private browser window for an explicitly authorized viewer.

The previously shared Workslip bearer token must never be used in this setup. Log out of old Workslip sessions and sign in again to invalidate/replace that session material.
