# Dependency map

**Status:** Generated — do not edit by hand
**Source:** `node tools/depmap/depmap.mjs` (verify freshness with `--check`)
**Purpose:** Module-level dependency and coupling map for boundary-split work ([WOR-443](https://linear.app/workslip/issue/WOR-443)). Regenerate after each split to confirm coupling actually went down.

## Backend — Application module coupling

Coupling = fan-in + fan-out between `Workslip.Application.*` modules. File refs = number of files importing across the boundary. Domain is the shared kernel and is excluded; Infrastructure implements Application ports and is listed separately.

| Module | Files | LOC | Fan-in | Fan-out | Inbound file refs | Outbound file refs | Coupling |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Auth | 5 | 263 | 8 | 1 | 20 | 2 | **9** |
| Jobs | 29 | 3270 | 4 | 3 | 6 | 13 | **7** |
| Users | 14 | 1671 | 4 | 2 | 8 | 4 | **6** |
| Images | 4 | 443 | 1 | 3 | 1 | 3 | **4** |
| Invitations | 6 | 726 | 0 | 3 | 0 | 7 | **3** |
| Worksheets | 5 | 515 | 1 | 2 | 4 | 4 | **3** |
| Customers | 7 | 562 | 0 | 2 | 0 | 2 | **2** |
| Notifications | 8 | 277 | 1 | 1 | 2 | 1 | **2** |
| Organizations | 7 | 594 | 0 | 2 | 0 | 4 | **2** |
| Common | 3 | 167 | 1 | 0 | 1 | 0 | **1** |
| Documents | 9 | 744 | 0 | 1 | 0 | 2 | **1** |
| Diagnostics | 1 | 90 | 0 | 0 | 0 | 0 | **0** |

### Cross-module edges (Application → Application)

| From → To | File refs |
| --- | --- |
| Jobs -> Auth | 7 |
| Jobs -> Worksheets | 4 |
| Invitations -> Auth | 3 |
| Invitations -> Users | 3 |
| Users -> Auth | 3 |
| Worksheets -> Jobs | 3 |
| Auth -> Users | 2 |
| Documents -> Auth | 2 |
| Jobs -> Notifications | 2 |
| Organizations -> Auth | 2 |
| Organizations -> Users | 2 |
| (root) -> Auth | 1 |
| (root) -> Customers | 1 |
| (root) -> Documents | 1 |
| (root) -> Images | 1 |
| (root) -> Invitations | 1 |
| (root) -> Jobs | 1 |
| (root) -> Notifications | 1 |
| (root) -> Organizations | 1 |
| (root) -> Users | 1 |
| (root) -> Worksheets | 1 |
| Customers -> Auth | 1 |
| Customers -> Jobs | 1 |
| Images -> Auth | 1 |
| Images -> Jobs | 1 |
| Images -> Users | 1 |
| Invitations -> Common | 1 |
| Notifications -> Jobs | 1 |
| Users -> Images | 1 |
| Worksheets -> Auth | 1 |

### Infrastructure → Application references

| From → To | File refs |
| --- | --- |
| Infra:Repositories -> App:Jobs | 9 |
| Infra:Repositories -> App:Auth | 5 |
| Infra:Repositories -> App:Users | 5 |
| Infra:(root) -> App:(root) | 3 |
| Infra:(root) -> App:Worksheets | 3 |
| Infra:Mappers -> App:Jobs | 3 |
| Infra:Notifications -> App:Notifications | 3 |
| Infra:Repositories -> App:Worksheets | 3 |
| Infra:Reporting -> App:Worksheets | 2 |
| Infra:Repositories -> App:Customers | 2 |
| Infra:Repositories -> App:Documents | 2 |
| Infra:Schema -> App:Auth | 2 |
| Infra:Storage -> App:Documents | 2 |
| Infra:Storage -> App:Images | 2 |
| Infra:Transactions -> App:Common | 2 |
| Infra:(root) -> App:Common | 1 |
| Infra:(root) -> App:Customers | 1 |
| Infra:(root) -> App:Diagnostics | 1 |
| Infra:(root) -> App:Documents | 1 |
| Infra:(root) -> App:Images | 1 |
| Infra:(root) -> App:Invitations | 1 |
| Infra:(root) -> App:Jobs | 1 |
| Infra:(root) -> App:Notifications | 1 |
| Infra:(root) -> App:Organizations | 1 |
| Infra:(root) -> App:Users | 1 |
| Infra:Diagnostics -> App:Diagnostics | 1 |
| Infra:Invitations -> App:Invitations | 1 |
| Infra:Jobs -> App:Images | 1 |
| Infra:Jobs -> App:Jobs | 1 |
| Infra:Repositories -> App:Common | 1 |
| Infra:Repositories -> App:Invitations | 1 |
| Infra:Repositories -> App:Notifications | 1 |
| Infra:Repositories -> App:Organizations | 1 |
| Infra:Resilience -> App:(root) | 1 |
| Infra:Schema -> App:Users | 1 |
| Infra:Schema -> App:Worksheets | 1 |

## Frontend — feature isolation

Cross-feature imports are boundary violations; shared refs (`lib/`, `hooks/`, `providers/`, …) are the sanctioned coupling path. Test files and `api/generated` are excluded.

| Feature | Files | LOC | Cross-feature imports | Shared imports |
| --- | --- | --- | --- | --- |
| customers | 6 | 1551 | **7** | 35 |
| settings | 3 | 671 | **4** | 8 |
| users | 7 | 1064 | **2** | 25 |
| auditor | 1 | 483 | **1** | 16 |
| jobs | 51 | 8288 | **1** | 156 |
| overview | 1 | 165 | **1** | 4 |
| superadmin | 16 | 3400 | **1** | 9 |
| auth | 10 | 1439 | 0 | 18 |
| create | 2 | 172 | 0 | 1 |
| docs | 3 | 828 | 0 | 12 |
| images | 6 | 532 | 0 | 4 |
| legal | 6 | 256 | 0 | 1 |
| worksheets | 6 | 933 | 0 | 11 |

### Cross-feature edges

| From → To | Imports |
| --- | --- |
| customers -> jobs | 7 |
| settings -> images | 4 |
| auditor -> jobs | 1 |
| jobs -> images | 1 |
| overview -> jobs | 1 |
| superadmin -> users | 1 |
| users -> jobs | 1 |
| users -> superadmin | 1 |

## God-file watchlist (largest files)

### Backend

| File | LOC |
| --- | --- |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Schema/SqlDbContext.cs` | 1485 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs` | 1077 |
| `src/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs` | 1067 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Schema/AuditPolicies.cs` | 926 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Diagnostics/ApplicationInsightsErrorDiagnosticsService.cs` | 879 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/JobReportPdfService.cs` | 679 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/DatabaseSeeder.cs` | 625 |
| `src/BE/WorkslipApi/Workslip.Application/Invitations/InvitationService.cs` | 548 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/PlatformIdentityBootstrapper.cs` | 453 |
| `src/BE/WorkslipApi/Workslip.Application/Users/SuperAdminUserService.cs` | 449 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfWorksheetRepository.cs` | 449 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Mappers/JobReportMapper.cs` | 437 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfCustomerRepository.cs` | 430 |
| `src/BE/WorkslipApi/Workslip.Application/Jobs/AuthorizedJobService.cs` | 423 |
| `src/BE/WorkslipApi/Configuration/LocalDevelopmentDatabaseMigrationRunner.cs` | 420 |

### Frontend

| File | LOC |
| --- | --- |
| `src/FE/src/features/jobs/hooks/useJobDetails.ts` | 800 |
| `src/FE/src/features/superadmin/routes/CacheDiagnostics.tsx` | 663 |
| `src/FE/src/features/jobs/routes/CompletedJobReport.tsx` | 625 |
| `src/FE/src/features/jobs/routes/JobList.tsx` | 554 |
| `src/FE/src/features/worksheets/routes/MyWorksheets.tsx` | 532 |
| `src/FE/src/features/jobs/components/JobDetails.tsx` | 511 |
| `src/FE/src/features/superadmin/diagnostics/ErrorDiagnosticsDashboard.tsx` | 490 |
| `src/FE/src/features/auditor/routes/AuditorReportList.tsx` | 483 |
| `src/FE/src/features/docs/DocsPage.tsx` | 476 |
| `src/FE/src/features/jobs/components/JobDetailBlocks.tsx` | 472 |
| `src/FE/src/features/users/routes/UserDetail.tsx` | 469 |
| `src/FE/src/features/customers/routes/CustomerList.tsx` | 466 |
| `src/FE/src/components/common/NotificationsDrawer.tsx` | 465 |
| `src/FE/src/features/superadmin/components/SuperAdminUsersPanel.tsx` | 453 |
| `src/FE/src/applicationInsights.ts` | 406 |

## Method

- Backend: each `.cs` file is assigned to a module by its declared namespace (`Workslip.<Layer>.<Module>`). Edges are distinct `using Workslip.*` targets per file, aggregated per module pair. `bin`, `obj` and `Workslip.Tests` are excluded.
- Frontend: each `.ts/.tsx` under `src/FE/src/features/<feature>` is scanned for relative imports; targets are classified as same-feature, cross-feature or shared.
- Namespace/using parsing is text-based, not Roslyn-based: fully-qualified type references without a `using` are not counted. Treat numbers as a consistent lower bound, good for trends, not an exhaustive census.
