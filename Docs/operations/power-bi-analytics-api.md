# Power BI analytics API

**Linear:** WOR-542

## Purpose

The Workslip analytics report reads production analytics through the Workslip HTTPS API rather than connecting directly to Azure SQL.

The Power BI-derived visualization exposed inside Workslip is intentionally limited to one Admin-only circular job-status chart on the Overview page. Power BI data/report UI must not be rendered on Timer or other Workslip pages.

## Production report source

Base URL:

`https://app.mrsoftware.dk`

Endpoint used by the PBIP semantic model:

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

## Workslip placement contract

The only Power BI analytics visualization inside Workslip is the circular **Sagsfordeling** chart on `/app/overblik`, and it is rendered only for the exact `Admin` frontend role.

The chart does not download the full analytics payload. It reads a dedicated, tenant-scoped summary endpoint:

`GET /api/power-bi/overview/job-status`

The summary returns only:

- total jobs
- Draft count
- InReview count
- Approved count
- Rejected count
- other/unknown status count
- generated timestamp

The summary endpoint requires the backend `RequireAdmin` policy and derives the organization from the authenticated user context. There is no organization id route/query input.

Timer keeps only its CSV/PDF worksheet export controls. No Power BI report link, iframe, report data, or Power BI-specific UI is rendered there.

## Validation boundary

Static validation verifies PBIP/PBIR/TMDL structure and model references. A full production report refresh still requires the analytics API to be deployed and Power BI authentication to succeed.

Workslip acceptance additionally verifies:

1. Admin Overview renders the circular job-status chart.
2. non-Admin Overview does not render or request the Power BI summary.
3. Timer contains no Power BI UI.
4. both Power BI endpoints remain tenant-scoped and Admin-protected on the backend.
