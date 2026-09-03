# Dependency map

**Status:** Generated — do not edit by hand
**Source:** `node tools/depmap/depmap.mjs` (verify freshness with `--check`)
**Purpose:** Module-level dependency and coupling map for boundary-split work ([WOR-443](https://linear.app/workslip/issue/WOR-443)). Regenerate after each split to confirm coupling actually went down.

## Backend — Application module coupling

Coupling = fan-in + fan-out between `Workslip.Application.*` modules. File refs = number of files importing across the boundary. Domain is the shared kernel and is excluded; Infrastructure implements Application ports and is listed separately.

| Module | Files | LOC | Fan-in | Fan-out | Inbound file refs | Outbound file refs | Coupling |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Auth | 5 | 263 | 11 | 1 | 25 | 2 | **12** |
| Jobs | 32 | 3835 | 5 | 3 | 7 | 18 | **8** |
| Users | 14 | 1673 | 4 | 2 | 8 | 4 | **6** |
| Conversations | 3 | 742 | 0 | 4 | 0 | 4 | **4** |
| Images | 4 | 455 | 1 | 3 | 1 | 3 | **4** |
| Invitations | 6 | 726 | 0 | 3 | 0 | 7 | **3** |
| Notifications | 8 | 486 | 2 | 1 | 4 | 1 | **3** |
| Worksheets | 5 | 515 | 1 | 2 | 6 | 4 | **3** |
| Common | 3 | 167 | 2 | 0 | 2 | 0 | **2** |
| Customers | 7 | 562 | 0 | 2 | 0 | 2 | **2** |
| Documents | 9 | 745 | 1 | 1 | 1 | 2 | **2** |
| Integrations | 6 | 321 | 1 | 1 | 2 | 1 | **2** |
| LeaderAnalysis | 3 | 97 | 0 | 2 | 0 | 3 | **2** |
| Organizations | 7 | 594 | 0 | 2 | 0 | 4 | **2** |
| Inventory | 1 | 352 | 0 | 1 | 0 | 1 | **1** |
| Diagnostics | 1 | 90 | 0 | 0 | 0 | 0 | **0** |
| ModuleAccess | 4 | 141 | 0 | 0 | 0 | 0 | **0** |
| Operations | 3 | 383 | 0 | 0 | 0 | 0 | **0** |

### Cross-module edges (Application → Application)

| From → To | File refs |
| --- | --- |
| Jobs -> Auth | 9 |
| Jobs -> Worksheets | 6 |
| Invitations -> Auth | 3 |
| Invitations -> Users | 3 |
| Jobs -> Notifications | 3 |
| Users -> Auth | 3 |
| Worksheets -> Jobs | 3 |
| Auth -> Users | 2 |
| Documents -> Auth | 2 |
| LeaderAnalysis -> Integrations | 2 |
| Organizations -> Auth | 2 |
| Organizations -> Users | 2 |
| (root) -> Auth | 1 |
| (root) -> Conversations | 1 |
| (root) -> Customers | 1 |
| (root) -> Documents | 1 |
| (root) -> Images | 1 |
| (root) -> Inventory | 1 |
| (root) -> Invitations | 1 |
| (root) -> Jobs | 1 |
| (root) -> ModuleAccess | 1 |
| (root) -> Notifications | 1 |
| (root) -> Organizations | 1 |
| (root) -> Users | 1 |
| (root) -> Worksheets | 1 |
| Conversations -> Auth | 1 |
| Conversations -> Common | 1 |
| Conversations -> Jobs | 1 |
| Conversations -> Notifications | 1 |
| Customers -> Auth | 1 |
| Customers -> Jobs | 1 |
| Images -> Auth | 1 |
| Images -> Jobs | 1 |
| Images -> Users | 1 |
| Integrations -> Documents | 1 |
| Inventory -> Auth | 1 |
| Invitations -> Common | 1 |
| LeaderAnalysis -> Auth | 1 |
| Notifications -> Jobs | 1 |
| Users -> Images | 1 |
| Worksheets -> Auth | 1 |

### Infrastructure → Application references

| From → To | File refs |
| --- | --- |
| Infra:Repositories -> App:Jobs | 9 |
| Infra:Repositories -> App:Auth | 5 |
| Infra:Repositories -> App:Users | 5 |
| Infra:Notifications -> App:Notifications | 4 |
| Infra:(root) -> App:(root) | 3 |
| Infra:Mappers -> App:Jobs | 3 |
| Infra:Repositories -> App:Worksheets | 3 |
| Infra:(root) -> App:Worksheets | 2 |
| Infra:Operations -> App:Diagnostics | 2 |
| Infra:Reporting -> App:Worksheets | 2 |
| Infra:Repositories -> App:Customers | 2 |
| Infra:Repositories -> App:Documents | 2 |
| Infra:Schema -> App:Auth | 2 |
| Infra:Storage -> App:Documents | 2 |
| Infra:Storage -> App:Images | 2 |
| Infra:Transactions -> App:Common | 2 |
| Infra:(root) -> App:Common | 1 |
| Infra:(root) -> App:Conversations | 1 |
| Infra:(root) -> App:Customers | 1 |
| Infra:(root) -> App:Diagnostics | 1 |
| Infra:(root) -> App:Documents | 1 |
| Infra:(root) -> App:Images | 1 |
| Infra:(root) -> App:Integrations | 1 |
| Infra:(root) -> App:Inventory | 1 |
| Infra:(root) -> App:Invitations | 1 |
| Infra:(root) -> App:Jobs | 1 |
| Infra:(root) -> App:LeaderAnalysis | 1 |
| Infra:(root) -> App:Notifications | 1 |
| Infra:(root) -> App:Operations | 1 |
| Infra:(root) -> App:Organizations | 1 |
| Infra:(root) -> App:Users | 1 |
| Infra:Diagnostics -> App:Diagnostics | 1 |
| Infra:Invitations -> App:Invitations | 1 |
| Infra:Jobs -> App:Images | 1 |
| Infra:Jobs -> App:Jobs | 1 |
| Infra:Operations -> App:Operations | 1 |
| Infra:Repositories -> App:Common | 1 |
| Infra:Repositories -> App:Conversations | 1 |
| Infra:Repositories -> App:Inventory | 1 |
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
| settings | 4 | 947 | **5** | 9 |
| jobs | 54 | 10238 | **2** | 193 |
| leader-analysis | 3 | 876 | **2** | 5 |
| overview | 2 | 466 | **1** | 10 |
| superadmin | 16 | 3398 | **1** | 10 |
| users | 8 | 1262 | **1** | 32 |
| auditor | 1 | 483 | 0 | 17 |
| auth | 11 | 1510 | 0 | 20 |
| create | 2 | 176 | 0 | 2 |
| customers | 6 | 1586 | 0 | 44 |
| docs | 5 | 917 | 0 | 19 |
| images | 7 | 629 | 0 | 5 |
| inventory | 2 | 714 | 0 | 6 |
| legal | 6 | 256 | 0 | 1 |
| worksheets | 6 | 1395 | 0 | 15 |

### Cross-feature edges

| From → To | Imports |
| --- | --- |
| settings -> images | 5 |
| jobs -> images | 2 |
| leader-analysis -> overview | 2 |
| overview -> docs | 1 |
| superadmin -> users | 1 |
| users -> superadmin | 1 |

## God-file watchlist (largest files)

### Backend

| File | LOC |
| --- | --- |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Schema/SqlDbContext.cs` | 1485 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs` | 1078 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Schema/AuditPolicies.cs` | 926 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Diagnostics/ApplicationInsightsErrorDiagnosticsService.cs` | 879 |
| `src/BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs` | 850 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/JobReportPdfService.cs` | 682 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/DatabaseSeeder.cs` | 625 |
| `src/BE/WorkslipApi/Workslip.Application/Conversations/JobConversationService.cs` | 587 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/SqlInventoryRepository.cs` | 575 |
| `src/BE/WorkslipApi/Workslip.Application/Invitations/InvitationService.cs` | 548 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/SqlJobConversationRepository.cs` | 471 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Seeding/PlatformIdentityBootstrapper.cs` | 453 |
| `src/BE/WorkslipApi/Workslip.Application/Users/SuperAdminUserService.cs` | 449 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfWorksheetRepository.cs` | 447 |
| `src/BE/WorkslipApi/Workslip.Infrastructure/Mappers/JobReportMapper.cs` | 438 |

### Frontend

| File | LOC |
| --- | --- |
| `src/FE/src/features/jobs/hooks/useJobDetails.ts` | 945 |
| `src/FE/src/features/worksheets/routes/MyWorksheets.tsx` | 907 |
| `src/FE/src/features/jobs/components/JobConversationDrawer.tsx` | 751 |
| `src/FE/src/features/jobs/routes/CompletedJobReport.tsx` | 704 |
| `src/FE/src/features/jobs/routes/AdminCompletedJobReport.tsx` | 671 |
| `src/FE/src/features/superadmin/routes/CacheDiagnostics.tsx` | 663 |
| `src/FE/src/components/common/NotificationsDrawer.tsx` | 563 |
| `src/FE/src/features/leader-analysis/routes/Lederanalyse.tsx` | 543 |
| `src/FE/src/features/docs/DocsPage.tsx` | 537 |
| `src/FE/src/features/customers/routes/CustomerList.tsx` | 505 |
| `src/FE/src/features/jobs/components/JobDetails.tsx` | 501 |
| `src/FE/src/features/users/routes/UserDetail.tsx` | 493 |
| `src/FE/src/features/superadmin/diagnostics/ErrorDiagnosticsDashboard.tsx` | 490 |
| `src/FE/src/features/jobs/components/JobDetailBlocks.tsx` | 484 |
| `src/FE/src/features/auditor/routes/AuditorReportList.tsx` | 483 |

## Method

- Backend: each `.cs` file is assigned to a module by its declared namespace (`Workslip.<Layer>.<Module>`). Edges are distinct `using Workslip.*` targets per file, aggregated per module pair. `bin`, `obj` and `Workslip.Tests` are excluded.
- Frontend: each `.ts/.tsx` under `src/FE/src/features/<feature>` is scanned for relative imports; targets are classified as same-feature, cross-feature or shared.
- Namespace/using parsing is text-based, not Roslyn-based: fully-qualified type references without a `using` are not counted. Treat numbers as a consistent lower bound, good for trends, not an exhaustive census.
