# Workslip samlet produkt- og implementeringsplan

> **For Hermes:** Implementér planen som små vertikale slices. Load `workslip-feature-development` før udførelse. Brug Linear til scope/status og repoet til kode/PR/CI/dokumentation. Opret aldrig opdigtede RBJ-numre.

**Status:** Foreslået og afstemt mod repository den 22. juli 2026  
**Mål:** Samle frontend-stabilisering, lager/jobmaterialer, isoleret demo og projektgovernance i én målbar leveranceplan.  
**Target repository:** `C:/Workslip`  
**Aktiv workspace:** `C:/Workslip/src`  
**Planens placering:** `src/docs/2026-07-22-workslip-samlet-produktplan.md`

## 1. Executive summary

Leverancen består af fire sammenhængende spor:

1. Stabiliser frontendens kendte P0-risici før større featurearbejde.
2. Byg tenant-isoleret lagerstyring og faktisk materialeforbrug som del af jobflowet.
3. Byg en isoleret, resetbar demo med rigtig Workslip-kode og Playwright-validering.
4. Ryd op i frontendens accessibility, arkitektur og vedligeholdelse uden et bredt redesign.

Den kritiske afhængighed er:

```text
Repo/Linear-afstemning
  -> Frontend P0 og workflow-afklaring
  -> Inventory-domæne og lagertransaktioner
  -> Jobmaterialer + atomisk submit/delta/reversal
  -> Demo af kun verificerede funktioner
  -> Frontend P1/P2, hardening og release
```

Planen må ikke implementeres som én stor branch. Hver slice skal have eget Linear-issue, lille PR, målrettet validering og dokumentation.

## 2. Verificeret repository-status

Følgende er verificeret direkte i nuværende checkout og er ikke kun antagelser fra tidligere ChatGPT-artifacts:

- Backend bruger .NET, EF Core og lagene `Workslip.Domain`, `Workslip.Application`, `Workslip.Infrastructure` og `Workslip.Api`.
- Aktive jobendpoints ligger i `BE/WorkslipApi/Endpoints/JobEndpoints.cs` under `/api/jobs`.
- Services returnerer `Ardalis.Result`; endpoints mapper via `ResultExtensions.ToHttpResult(...)`.
- `JobStatus` har kun `Draft`, `InReview`, `Approved` og `Rejected` i `BE/WorkslipApi/Workslip.Domain/JobStatus.cs`.
- Jobstatus ændres via `JobService.ChangeStatusAsync(...)` og repository-transition. Der findes ikke endnu en verificeret atomisk inventory-posting boundary.
- Der findes ingen inventory/material-domainmodeller i nuværende checkout.
- Customer-edit-routen bruger fejlagtigt `user:manage` i `FE/src/routes/index.tsx:112`; create-routen bruger `customer:edit`.
- PWA er konfigureret med `registerType: 'autoUpdate'` i `FE/vite.config.ts`.
- `FE/src/registerSW.ts` kalder `updateSW()` straks ved ny version.
- `FE/src/sw.ts` kalder `skipWaiting()`, `clientsClaim()` og sender `RELOAD` til åbne klienter.
- `AuditorReportList.tsx` henter sider fra serveren, men søger/sorterer/paginerer derefter kun over allerede indlæste `query.items`.
- Routeren importerer alle større routes statisk; route-level lazy loading er ikke implementeret.
- To `AppLayout.tsx` findes. Routeren bruger `FE/src/components/layouts/AppLayout.tsx`; `FE/src/layouts/AppLayout.tsx` ligner gammel/død implementation.
- Delt `NumericInput` findes i `FE/src/components/forms/NumericInput.tsx`.
- Frontend har kun én synlig testfil og ingen verificeret Playwright-konfiguration.
- Demo/deployment-infrastruktur findes under `BE/infrastructure/*.bicep`, men en dedikeret demo-environment er ikke verificeret.
- Nuværende user changes skal bevares urørt:
  - staged: `C:/Workslip/.kiro/specs/job-seen-indicator/tasks.md`
  - modified: `FE/src/features/jobs/routes/JobList.tsx`
  - modified: `FE/src/hooks/usePaginatedList.ts`

## 3. Produktregler og arkitekturkontrakter

### 3.1 Fælles kontrakter

- Linear er source of truth for scope, prioritet, acceptance criteria og status.
- Repoet er source of truth for kode, database, API, tests, PR’er og stabile beslutninger.
- Repomix er orientering, ikke erstatning for aktuelle filer.
- Endpoints er tynde; domæneregler ligger i Application services/commands.
- Tenant-id kommer fra autentificeret servercontext, aldrig trusted client input.
- Authorization håndhæves på serveren. UI-guards er ekstra UX, ikke sikkerhed.
- `CancellationToken` bevares gennem hele backend-kæden.
- Frontend bruger eksisterende components og API/query-mønstre. `NumericInput` bruges til decimaltal og skal bevare dansk decimalkomma.
- Ingen filer omdøbes eller flyttes som sideeffekt af denne plan.
- Hver PR indeholder relevant dokumentation og validering; dokumentation udskydes ikke til slutningen.

### 3.2 Inventory/jobmaterialer

- Draft-materialelinjer påvirker ikke lager.
- Submit og lagerpostering skal ske atomisk i samme databasetransaktion.
- Klienten må aldrig køre “post stock” efterfulgt af “submit job” som to separate commands.
- En retried request må ikke skabe dobbelt lagertræk.
- Lager må ikke blive negativt, heller ikke ved samtidige submits.
- Materialebevægelser er append-only.
- Balance er et transactionelt read model, der skal kunne reconciles mod movement-loggen.
- Returned/rejected flow poster kun `desired quantity - posted quantity` ved resubmit.
- Reduceret mængde giver positiv lagerbevægelse.
- Fjernet tidligere postet linje repræsenteres som ønsket quantity `0`, ikke som tavs historiksletning.
- Cancellation/reversal skal være eksplicit, kræve reason og ske præcis én gang.
- Materialenavn, SKU, enhed og kostpris snapshots bevares historisk efter deaktivering.
- Cost visibility er en serverpermission, ikke kun frontend-hiding.

### 3.3 Demo

- Demo bruger rigtig Workslip frontend/backend, ikke en parallel mock-app.
- Demo har egen database, tenantgrænse og integration configuration uden produktionsconnectivity.
- Alle viste data er deterministiske og fiktive.
- Persona-adgang bruger korte sessions og må ikke lække genbrugelige credentials til frontend source.
- Reset er idempotent, begrænset til den allokerede demo-tenant og ikke registreret i produktion.
- Iframe er kun optional marketing preview. Primær demo åbner direkte.
- Demo må kun vise funktioner, der findes og er valideret i deployet kode.
- Browservalidering køres mod fuld demo-URL.

## 4. Scope og leverancer

## Workstream A — Intake, statusmaskine og Linear

### A1. Beskyt checkout og opret execution baseline

**Mål:** Få sporbar start uden at blande eksisterende user changes ind.

**Handlinger:**

- Læs `git status` før hver slice.
- Kortlæg nærmeste gældende repo-regler og eksisterende docs.
- Opret/afstem Linear-issues for hver slice; brug kun rigtige IDs.
- Dokumentér afhængigheder mellem frontend P0, inventory og demo.
- Marker gamle artifact-datoer/coding-holds som historik, ikke permanente regler.

**Måling:**

- 100 % af implementation-slices har Linear-issue, owner, acceptance criteria og dependency.
- Ingen eksisterende user-modified fil ændres af intakearbejdet.

### A2. Fastlæg faktisk job-workflow

**Mål:** Stoppe inventory/demo i at koble sig til opdigtede statusser.

**Filer til analyse:**

- `BE/WorkslipApi/Workslip.Domain/JobStatus.cs`
- `BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs`
- `BE/WorkslipApi/Workslip.Application/Jobs/JobValidationService.cs`
- `BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs`
- `BE/WorkslipApi/Endpoints/JobEndpoints.cs`
- relevante frontend status-actions og `FE/src/features/jobs/statusLabels.ts`

**Beslutninger der skal låses:**

- Betydningen af `Rejected`: returned-for-correction, endelig afvisning eller begge.
- Hvilken transition repræsenterer submit, review, resubmit og cancellation.
- Om cancellation skal være ny status, separat command eller deletion/reversal-state.
- Transaction boundary og idempotency-model for submit.

**Måling:**

- Ét godkendt workflowdiagram og transitionmatrix.
- Ingen inventory/demo-code starter før matrixen er afstemt.

## Workstream B — Frontend P0

### B1. Customer edit permission

**Mål:** Brugere med `customer:edit` kan redigere kunder; `user:manage` giver ikke utilsigtet adgang.

**Primære filer:**

- `FE/src/routes/index.tsx`
- `FE/src/features/customers/routes/CustomerList.tsx`
- `FE/src/features/customers/routes/CustomerDetail.tsx`
- målrettet route/permission testfil

**Acceptance criteria:**

- Edit-route og synlige edit-actions bruger `customer:edit` konsekvent.
- Backendpermission verificeres separat.
- Én målrettet regressionstest beviser route-guard-adfærd.

### B2. Dirty-safe PWA update

**Mål:** Ny deployment må ikke automatisk reloade en dirty, lang formular.

**Primære filer:**

- `FE/vite.config.ts`
- `FE/src/registerSW.ts`
- `FE/src/sw.ts`
- eksisterende navigation/dirty-form providers og jobformularer
- ny central update-prompt/state component

**Acceptance criteria:**

- Ny version vises som “En ny version er klar”.
- Update kræver eksplicit brugeraccept.
- Update-action er blokeret eller gemmer lokalt draft, mens formular er dirty.
- Service worker overtager ikke og reloader ikke ukontrolleret.
- Regressionstest dækker dirty og clean flow.

### B3. Server-korrekt auditor search/sort/pagination

**Mål:** Søgeresultater og sortering repræsenterer hele tenantens dataset, ikke kun hentede pages.

**Primære filer:**

- `BE/WorkslipApi/Endpoints/JobEndpoints.cs`
- `BE/WorkslipApi/Workslip.Application/Jobs/JobService.cs`
- `BE/WorkslipApi/Workslip.Application/Jobs/IJobRepository.cs`
- `BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs`
- `FE/src/features/auditor/routes/AuditorReportList.tsx`
- relevante backend integration/API tests

**Acceptance criteria:**

- Search, status, sort, limit og offset sendes til API.
- Backend sorterer stabilt før `Skip/Take`.
- `totalCount` kommer fra det filtrerede serversæt.
- Rapport uden for første page findes uden prefetch af tidligere pages.
- Desktop og mobil bruger samme servertruth.

### B4. Honest offline + sikker draft

**Mål:** Workslip lover ikke offline-submit, før det er sikkert; lange drafts overlever connection loss/reload.

**Første version:**

- Global online/offline-indikator.
- Lokalt draft i IndexedDB med tenant/user/job-namespacing.
- “Afventer synkronisering”, “Synkroniseret” og “Kræver handling”.
- Submit køres kun online.
- Ingen lydløs konflikt-overwrite.

**Ikke i første version:** Fuld generisk mutation queue for alle features.

**Acceptance criteria:**

- Draft kan genåbnes efter reload/offline.
- Tenant/user-cross-over er umuligt.
- Fejlet submit beholder input.
- UI siger tydeligt, hvad der kun er gemt lokalt.

## Workstream C — Inventory og jobmaterialer

### C1. ADR og persistensmodel

**Mål:** Fastlæg atomisk posting, delta, reversal og reconciliation før endpoints/UI.

**Foreslåede nye domænebegreber:**

- `MaterialCatalogItem`
- `StockLocation`
- `StockBalance`
- `StockMovement`
- `JobMaterialLine`
- `InventoryPostingBatch`

**Foreslåede placeringer, endeligt navn afstemmes med eksisterende conventions:**

- `BE/WorkslipApi/Workslip.Domain/Models/`
- `BE/WorkslipApi/Workslip.Application/Inventory/`
- `BE/WorkslipApi/Workslip.Infrastructure/Repositories/`
- `BE/WorkslipApi/Workslip.Infrastructure/Schema/SqlDbContext.cs`
- EF migration under projektets eksisterende migration convention
- ADR under `docs/`

**Datakrav:**

- Alle rows har tenant/organization ownership.
- Unik balance-key: organization + material + location.
- Decimal precision vælges ud fra eksisterende conventions.
- Optimistic concurrency/row version på balance og relevante lines.
- Unik idempotency/posting key.
- Snapshot fields på jobmaterialelinjer/movements.
- Append-only movements med actor, occurredAt, reason og correlation.

**Acceptance criteria:**

- Migration bygger og kan anvendes på tom database.
- Tenant- og uniqueness-constraints er databasehåndhævet.
- ADR beskriver transaction boundary, delta og reversal.

### C2. Catalogue og stock locations

**Mål:** Admin/inventory-manager kan administrere materialer og lokationer uden at ødelægge historik.

**API-shape, endelig routing følger repo-convention:**

- `GET/POST/PATCH /api/inventory/materials`
- `GET/POST/PATCH /api/inventory/locations`

**Acceptance criteria:**

- SKU uniqueness-policy er dokumenteret og testet.
- Deaktiverede entities kan ses historisk, men ikke bruges i nye writes.
- Cross-tenant IDs afvises uden disclosure.
- Thin endpoints mapper `Ardalis.Result` via eksisterende helpers.

### C3. Stock commands og movement history

**Mål:** Modtagelse, adjustment og transfer er atomiske og auditable.

**API-shape:**

- `GET /api/inventory/balances`
- `GET /api/inventory/movements`
- `POST /api/inventory/receipts`
- `POST /api/inventory/adjustments`
- `POST /api/inventory/transfers`

**Acceptance criteria:**

- Transfer laver lige stor negativ/positiv movement-pair i én transaktion.
- Adjustment/reversal kræver reason.
- Duplicate command skaber ikke duplicate movements.
- Concurrent commands kan ikke skabe negativ balance.
- Conflict returneres eksplicit; ingen silent overwrite.

### C4. Inventory administration UI

**Mål:** Responsive administration af catalogue, locations, balances og movements.

**Foreslået frontendområde:**

- `FE/src/features/inventory/`
- routes i `FE/src/routes/index.tsx`
- permission-aware navigation i `FE/src/components/layouts/AppLayout.tsx`
- API/query hooks efter eksisterende `apiClient`/React Query pattern

**Acceptance criteria:**

- Search, status, SKU, unit, available total og low-stock state.
- Forms bruger shared components og `NumericInput`.
- Receipt/adjustment/transfer har explicit confirmation.
- Loading, empty, retry, validation, permission denied og conflict states findes.
- Mobile flow kan bruges i marken.

### C5. Draft jobmaterialer

**Mål:** Medarbejder kan registrere ønsket materialeforbrug på editable job uden lagerpåvirkning.

**Foreslået API-shape:**

- `GET /api/jobs/{jobId}/materials`
- `PUT /api/jobs/{jobId}/materials`

**Frontend:**

- Ny “Materialer”-step umiddelbart før review/submit; faktisk position verificeres mod current job-step implementation.
- Search active catalogue, vælg allowed location, vis unit og availability.
- Quantity via `NumericInput`.
- Edit/remove draft lines og optional note.
- Review summary viser antal og total; cost kun med permission.

**Acceptance criteria:**

- Draft writes ændrer ingen balance/movement.
- Duplicate material/location lines merges eller afvises deterministisk.
- Zero/negative quantities afvises.
- Deaktiverede references vises historisk, men kan ikke vælges nyt.
- Unsaved-change protection bevares.

### C6. Atomisk submit/posting

**Mål:** Jobstatus og stock consumption lykkes eller fejler sammen.

**Required transaction steps:**

1. Authorize job, locations og tenant ownership.
2. Validér faktisk workflow-state.
3. Validér positive quantities og active references.
4. Beregn delta fra `PostedQuantity`.
5. Lås/beskyt berørte balances.
6. Afvis hele command hvis en balance bliver negativ.
7. Append movements med posting batch/correlation.
8. Snapshot name/unit/cost.
9. Opdatér `PostedQuantity`.
10. Transition job status.
11. Commit én transaction.

**Acceptance criteria:**

- Retry trækker ikke lager igen.
- Insufficient stock ruller jobstatus, balances og movements tilbage.
- To samtidige submits kan ikke presse balance under nul.
- API returnerer forståelig validation/conflict til frontend.

### C7. Return/resubmit og cancellation reversal

**Mål:** Korrekt delta og sporbar reversal.

**Acceptance criteria:**

- Resubmit efter `Rejected` poster kun ændringen.
- Reduceret/fjernet line returnerer præcis korrekt quantity.
- Approval skaber ingen inventory movement.
- Cancellation reverserer remaining posted quantities præcis én gang.
- Actor, timestamp, reason, job og original posting relation gemmes.
- Jobstatus/reversal sker i samme transaction.

### C8. Hardening og reconciliation

**Mål:** Driftsklarhed og dokumenteret correctness.

**Acceptance criteria:**

- Reconciliation kan sammenligne balances med movement sums.
- Structured logs/metrics findes for conflicts, insufficient stock, duplicate commands og reconciliation failures.
- ETag/cache invalidation opdateres for affected jobs/balances/movements.
- API/Postman integration collection dokumenterer kritiske flows.

## Workstream D — Isoleret demo

### D1. Demo environment

**Mål:** Deploy rigtig kode uden produktionsconnectivity.

**Primære områder:**

- `BE/infrastructure/*.bicep`
- backend environment configuration
- frontend environment configuration/deployment
- deployment docs under `docs/`

**Acceptance criteria:**

- Egen demo API/database/tenant boundary.
- Ingen production secrets, mail, billing eller destructive integrations.
- Demo-banner er synligt.
- Logs indeholder ingen sensitiv data.
- Rate limiting og korte sessions er konfigureret.

### D2. Deterministisk seed og reset

**Mål:** Hver demo/test starter fra forudsigelig state.

**Seed minimum:**

- 5 employees.
- 3 customers.
- 4 active jobs/projects efter faktisk domænemodel.
- Draft, review, rejected/correction og approved examples, men kun verificerede statusser.
- Realistiske worksheets og senere materialer.
- Ét incomplete validation example.
- Cross-tenant fixture kun til automated isolation tests.

**Reset:**

- Internal demo/test-only command eller endpoint.
- Separat intern authorization.
- Arbitrary tenant IDs afvises.
- Idempotent og concurrency-safe.
- Må aldrig registreres i production.

### D3. Persona entry og guided flow

**Personaer:** Employee, supervisor, project manager og administrator.

**Acceptance criteria:**

- Ingen offentlig registration.
- Short-lived session med safe fixed permissions.
- Account/password/billing/destructive settings er disabled.
- Guidance er presentation-only og bypasser ikke normale APIs/permissions.

### D4. Playwright og marketing entry

**Foreslået struktur:**

```text
FE/tests/e2e/
  fixtures/
  employee/
  supervisor/
  manager/
  security/
  accessibility/
```

**Kritiske journeys:**

- Employee: vælg job -> timer/materialer -> draft -> submit.
- Supervisor: review -> reject/correction -> approve corrected job.
- Manager: projekt/periode -> approved work -> summary/export-ready view.
- Security: tenant isolation og authorization.
- Accessibility: keyboard, focus, labels og announced errors.

**Browsermatrix:**

- Chromium desktop.
- Chromium mobile viewport.
- WebKit/iPhone viewport for kritiske flows.

**Failure artifacts:** trace, screenshot, console, failed requests og video for kritiske flows.

**Marketing:** Optional sandboxed iframe-preview plus prominent “Åbn fuld demo”. CSP, cookies, SameSite, frame-ancestors og fallback verificeres før iframe aktiveres.

## Workstream E — Frontend P1/P2

### E1. Accessible combobox/dropdowns

**Primære filer:**

- `FE/src/components/forms/SingleSelectDropdown.tsx`
- `FE/src/components/forms/MultiSelectDropdown.tsx`
- address autocomplete implementations

**Acceptance criteria:**

- Korrekt combobox/listbox semantics.
- Arrow keys, Home/End, Escape og Enter.
- Active option, focus return og skærmlæserannoncering.
- Én fælles pattern; ingen nye næsten-identiske specialimplementeringer.

### E2. Standardiser lange formularer

**Mål:** Ens dirty/draft/submission/error behavior.

**Acceptance criteria:**

- Standard for touched, dirty, submitting og submitted.
- Errors vises efter blur/submit, ikke aggressivt under typing.
- Error summary og fokus på første fejl ved submit.
- Ens server-error mapping og save status.
- Shared payload builders pr. flow.

### E3. Navigation og primære actions

**Mål:** Én klar primær handling pr. skærm.

**Acceptance criteria:**

- Global create-FAB konkurrerer ikke med Gem/Fortsæt/Indsend/Godkend/Afvis.
- Jobflowets sticky navigation og bottom nav overlapper ikke.
- Mobile keyboard/focus states valideres.

### E4. Route code splitting

**Mål:** Montører downloader ikke auditor/admin kode ved start.

**Prioritet:** auditor, settings, user management, customer management og completed/PDF views.

**Acceptance criteria:**

- Lazy route chunks verificeret i build output.
- Loading/error states findes ved chunk load.
- Auth/permission guards bevares.

### E5. API/query consistency

**Mål:** Reducér runtime drift mellem genererede contracts og manuelle casts.

**Kontrakt:**

- Orval-generated request functions som standard.
- Thin feature-query layer til React Query.
- Query-key factories/prefix-compatible invalidation.
- Ingen manuelle response-casts i store route components.

### E6. Fjern duplicate layout og split global CSS

**Mål:** Fjern dokumenteret dead code og feature-CSS coupling uden redesign.

**Filer:**

- Aktiv: `FE/src/components/layouts/AppLayout.tsx`
- Kandidat til dokumenteret removal: `FE/src/layouts/AppLayout.tsx`
- `FE/src/App.css`
- nye scoped CSS-filer efter verificeret importpattern

**Acceptance criteria:**

- Ingen runtime/import references til gammel layout.
- Removal sker i egen PR og omdøber ikke andre filer.
- Feature-specifik CSS flyttes gradvist, ikke via big-bang rewrite.
- Visual smoke test på mobile/desktop kritiske screens.

## 5. Test- og valideringsstrategi

### Backend

Foretræk integration/API-niveau. Kritiske inventory-tests skal bevise:

- submit deducts præcis én gang;
- insufficient stock ruller alt tilbage;
- retry skaber ingen duplicate movement;
- concurrent submits kan ikke skabe negativ stock;
- resubmit poster kun delta;
- cancellation reverserer præcis én gang;
- transfer laver balanced pair;
- cross-tenant identifiers afvises;
- deaktiverede entities bevares historisk.

Kommandoer:

```bash
cd C:/Workslip/src/BE/WorkslipApi
dotnet build Workslip.Api.csproj --no-restore -v minimal -o C:/Workslip/.tmp/api-build-<timestamp>
dotnet test Workslip.Tests/Workslip.Tests.csproj --no-restore --logger "console;verbosity=minimal" -p:OutputPath=C:/Workslip/.tmp/test-out/<timestamp>/
```

Postman/API-verifikation skal dække happy path, validation, permission, conflict, retry og tenant isolation.

### Frontend

```bash
cd C:/Workslip/src/FE
npm exec tsc -- -b --pretty false
npm run lint --silent
npm run build:local --silent
```

Tilføj kun tests med reel regressionsværdi: permission, PWA dirty-update, auditor servertruth, offline draft, inventory critical flow, combobox keyboard og failed mutation input retention.

### Demo/E2E

- Playwright mod local, PR preview og deployed demo.
- Tests provisionerer/resetter eget datasæt.
- Tests afhænger ikke af rækkefølge.
- Failure artifacts uploades i CI.

## 6. Målbar tidsplan

### Estimatforudsætninger

- Start: 23. juli 2026.
- Én aktiv implementation stream ad gangen.
- 50 arbejdsdage inkluderer review, målrettede tests, dokumentation og 20 % risikobuffer.
- Linear-afklaringer og product review besvares samme arbejdsdag.
- Ingen større Azure/Entra procurement-, tenant- eller budgetblokering.
- Planen tæller ikke fuldt purchasing/accounting-system, barcode, valuation eller offline sync af alle mutationer.

| Fase | Dato | Arbejdsdage | Målbar exit gate |
|---|---:|---:|---|
| 0. Repo/Linear/workflow-afstemning | 23.–24. jul. | 2 | Alle slices har issues; statusmatrix og transaction decision er godkendt |
| 1. Frontend P0 | 27. jul.–3. aug. | 6 | 4 P0-slices merged; regression checks grønne |
| 2. Inventory/jobmaterialer | 4.–27. aug. | 18 | C1–C8 merged; atomisk submit/delta/reversal bevist via API/integration |
| 3. Demo platform | 28. aug.–10. sep. | 10 | Isoleret demo deployet; seed/reset/personaer og kritiske Playwright flows grønne |
| 4. Frontend P1/P2 | 11.–22. sep. | 8 | E1–E6 merged; accessibility/build/smoke checks grønne |
| 5. Release, docs og hardening | 23.–30. sep. | 6 | Reconciliation, observability, Postman, docs, demo E2E og release checklist godkendt |

**Baseline target:** 30. september 2026.  
**Forecast rule:** Target flyttes kun ved dokumenteret scopeændring eller ekstern blocker. Hver blocker registreres med owner, startdato, påvirkede gates og nyt forecast.

### Ugentlig progress score

Rapportér hver fredag:

- Planned slices / merged slices.
- Acceptance criteria passed / total.
- Grønne kritiske backend tests.
- Grønne kritiske browserflows.
- Åbne P0/P1 defects.
- Blocked days og årsag.
- Forecast: grøn, gul eller rød.

Farver:

- **Grøn:** Alle aktuelle phase gates forventes færdige inden planlagt dato.
- **Gul:** 1–3 arbejdsdages risiko eller én uløst ekstern dependency.
- **Rød:** Mere end 3 arbejdsdages slip, security/data-integrity defect eller uklar transaction/workflow decision.

## 7. Global definition of done

Leverancen er først færdig, når alle punkter er sande:

- Frontend P0-bugs er rettet og regressionstestet.
- Inventory er tenant-isoleret og lager kan ikke blive negativt.
- Draft-materialer ændrer ikke stock.
- Submit og stock posting er én atomisk, idempotent operation.
- Return/resubmit poster kun delta.
- Cancellation reverserer præcis én gang med reason/audit.
- Balance reconciles mod movements.
- Demo har ingen production connectivity eller secrets.
- Demo reset er sikker og deterministisk.
- Kritiske persona-, security- og accessibility-flows er grønne i Playwright.
- Frontend P1/P2 acceptance criteria er opfyldt uden bredt redesign.
- Backend build/tests, frontend typecheck/lint/build og Postman-verifikation er grønne.
- Linear-status, repo docs og deployment docs matcher leveret adfærd.
- Ingen eksisterende user changes er overskrevet.
- Ingen opdigtede RBJ-numre eller skjulte scope additions.

## 8. Explicit non-goals

Ikke med i baseline-target uden nyt Linear-scope og reforecast:

- Purchase orders, suppliers, invoice matching eller accounting integration.
- Barcode/QR scanning.
- Serial/batch/expiry/warranty tracking.
- FIFO/weighted-average valuation.
- Generisk offline mutation engine for hele Workslip.
- Material reservations/planning.
- Analytics for profitability/shrinkage/variance.
- Komplet redesign eller nyt CSS/component framework.
- Iframe som primær demo-runtime.

## 9. Første execution checkpoint

Før første kodeændring:

1. Beskyt nuværende modified/staged files.
2. Opret/afstem Linear-issues uden opdigtede IDs.
3. Godkend jobstatus/transitionmatrix.
4. Godkend inventory transaction/idempotency ADR.
5. Start med customer permission og dirty-safe PWA som separate små PR’er.
6. Reforecast efter fase 0 med faktiske Linear-dependencies og Azure-demo constraints.
