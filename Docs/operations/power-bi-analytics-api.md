# Power BI analytics API

**Linear:** WOR-542

## Purpose

The Workslip analytics report must read production analytics through the Workslip HTTPS API rather than connect directly to Azure SQL.

This is the source contract used by the validated Workslip Analytics PBIP project.

## Production source

Base URL:

`https://app.mrsoftware.dk`

Endpoint:

`GET /api/worksheets/all/report/power-bi/data?historyMonths=24`

The endpoint is Admin-only and tenant-scoped from the authenticated Workslip user context. Power BI does not send or choose an organization id.

Response collections:

- `employees`
- `workHours`
- `jobs`
- `customers`

Metadata:

- `schemaVersion`
- `generatedAtUtc`
- `historyMonths`

## Power BI connection

The semantic model uses one shared `AnalyticsApi` Power Query expression and reuses the JSON response for all API-backed tables.

```powerquery
Json.Document(
    Web.Contents(
        #"ApiBaseUrl",
        [
            RelativePath = "api/worksheets/all/report/power-bi/data",
            Query = [historyMonths = Text.From(#"HistoryMonths")],
            Headers = [Accept = "application/json"]
        ]
    )
)
```

Parameters:

- `ApiBaseUrl = "https://app.mrsoftware.dk"`
- `HistoryMonths = 24`

Do not add `Sql.Database` or `Value.NativeQuery` to the report. No SQL credentials, API keys, bearer tokens, or passwords may be hardcoded in the PBIP project.

## Report publishing and Workslip embed

1. Open the validated Workslip Analytics PBIP project in Power BI Desktop.
2. Authenticate the Workslip web data source with the intended Microsoft organizational identity that has Workslip Admin access.
3. Refresh and validate the `employees`, `workHours`, `jobs`, and `customers` tables.
4. Publish the report to the intended authenticated Power BI workspace.
5. Configure `PowerBiReport:Url` with the published normal `https://app.powerbi.com/...` report URL.
6. Workslip displays the secure authenticated report in the Admin Timer UI. Publish-to-web is not allowed.

The frontend does not duplicate analytics data. Its responsibility is embedding the already-published Power BI report. The PBIP report itself owns the API data connection.

## Validation boundary

Static validation verifies PBIP/PBIR/TMDL structure and model references. A full production refresh requires this API endpoint to be merged/deployed and Power BI authentication to succeed.
