# WOR-659 — SuperAdmin Statistik

State: Active
Last verified: 2026-09-30
Primary implementation PR: #1083

## Formål

Denne side dokumenterer den tekniske rejse og den nuværende adfærd for den native SuperAdmin-statistik i Workslip. Dokumentet beskriver de grænser og beslutninger, som er dyre at genfinde: datakilde, persistence, opdateringskadence, rejection-kategorier, aktiv-tidsmåling og den release-evidens der blev brugt til at validere løsningen.

## Nuværende produktadfærd

SuperAdmin har en separat Statistik-side under `/superadmin/statistik`. Siden læser statistik fra backend-endpointet `/api/superadmin/analytics/workflow-statistics` og bruger den vedvarende Workslip-database som datakilde.

Dashboardet opdaterer automatisk data hvert 60. sekund. Brugeren kan også udløse en manuel opdatering. Pollingfrekvensen er bevidst holdt på 60 sekunder for at følge den eksisterende systempraksis og undgå unødvendig belastning på statistik-endpointet.

Statistikken er ikke baseret på local storage, mock-data eller midlertidig React-state som sandhedskilde. Frontend-state bruges kun til præsentation og query-cache; den autoritative data ligger i backend-databasen.

## Dataplatform og persistence

Workslip backend anvender SQL Server via Entity Framework Core. Runtime-forbindelsen løses fra `Azure:Sql:ConnectionString`.

I live-miljøet er den relevante databasekonfiguration:

- Azure resource group: `rg-mrsoftwarev2-live`
- Azure SQL server: `db-mrsoftwarev2-live-server`
- Azure SQL database: `db-mrsoftwarev2-live`
- Azure region: `swedencentral`

Statistik-endpointet beregner resultater ud fra de eksisterende Workslip-tabeller og audit-events. Der er ikke oprettet en separat statistikdatabase.

## Aktiv medarbejdertid

Aktiv udfyldelsestid registreres fra medarbejderflowet som korte segmenter. Frontend flusher aktive segmenter cirka hvert 10. sekund samt ved relevante livscyklushændelser som blur/hidden/idle.

Hvert segment får et klientgenereret `segmentId`. Backend bruger dette ID som event-ID i `JobEvents`. Det gør registreringen idempotent: samme segment kan retries uden at tælle dobbelt. Hvis et eksisterende ID tilhører en anden organisation, sag eller eventtype, returneres konflikt i stedet for at genbruge eventet.

Eventtypen er `analytics.employee_active_segment`. Segmenterne er analytics-telemetri og må ikke blokere det normale job-flow, hvis telemetry-kaldet fejler.

## Afvisningskategorier

Admin-afvisningsflowet gemmer en struktureret `rejectionCategory` i det eksisterende audit-event i `JobEvents` i samme transaktion som statusændringen. Den rene lederkommentar forbliver i `RejectionNote`, så intern klassifikationsmetadata ikke blandes ind i medarbejderens synlige kommentar.

Statistikken bruger den strukturerede kategori, når den findes. For historiske sager uden struktureret kategori bruges den eksisterende fritekstklassifikation som fallback. Dermed kan nyere data blive mere præcis uden at ældre afvisninger forsvinder fra statistikken.

De vedligeholdte kategorier er blandt andet manglende data, forkert måling/værdi, manglende billeddokumentation, forkert kontrolpunkt, ufuldstændigt arbejde, dublet/forkert sag, system/UI-signal, andet og uklassificeret.

## Rejsen fra første implementation til stabil version

Den første native Statistik-side erstattede behovet for den gamle produktivitets/analytics-blok på SuperAdmin-forsiden. Under den efterfølgende stabilisering blev den gamle blok fjernet, så SuperAdmin kun har én autoritativ statistikoplevelse.

Datafundamentet blev derefter gjort mere vedvarende. Afvisningsårsager blev flyttet fra ren fritekstafledning til struktureret audit-metadata med historisk fallback. Aktiv tid blev gjort idempotent med `segmentId` og hyppigere flush, så retries ikke kan overrapportere tid.

Den første browser-verifikation af den nye statistik fejlede, selv om API'et svarede korrekt. Fejlen lå i selve Playwright-scenariet: testen ventede på den udgåede selector `.superadmin-statistics-page`, mens den rigtige side bruger `.statistics-page`. Testen blev rettet til de aktuelle UI-selectors og udvidet til at validere den reelle SuperAdmin error-state, KPI-kort, API-status og responsive layout.

Efter rettelsen blev hele exact-head CI-kørslen grøn.

## Valideringsstrategi

Valideringen følger repository-reglerne i `Docs/agents/VALIDATION.md`: persisted multi-endpoint flows valideres via API/integrationstests, mens den bruger-synlige kritiske rejse valideres med Playwright.

Den endelige verifikation omfattede:

- backend restore, Release build og test
- frontend unit/regression tests
- frontend production build og API contract
- lint-debt gate
- contracts + docs
- Postman integration mod en disposable SQL Server
- autentificeret Playwright-suite mod disposable SQL Server og rigtig backend
- Repository Data Hygiene
- samlet CI Gate

Browser-evidensen dækkede blandt andet:

- opret sag → medarbejder indsender → admin godkender
- opret sag → indsend → afvis → ret → genindsend → godkend
- reopen- og withdraw-flows
- notifications/people-lifecycle
- duplicate assignment
- SuperAdmin Statistik på desktop 1280 og mobil 390
- ingen page errors, console errors eller fejlede API-responses i Statistik-scenariet
- persisted struktureret afvisningskategori læst tilbage gennem statistik-API'et

På den verificerede exact head `b84c83530aefafc80d0c59f5288c62d2536ee393` bestod backend 877/877 tests og frontend 500/500 tests. CI run `36707840716` og Repository Data Hygiene run `36707840672` afsluttede begge med success.

## Hvad 60-sekunders opdatering betyder

Statistik-siden anvender polling med `refetchInterval: 60_000`. Det betyder, at nye eller ændrede databaseværdier bliver læst ind i dashboardet ved næste pollingrunde uden manuel reload af siden.

Det er near-real-time polling, ikke server-push. Der anvendes ikke SignalR/WebSocket til denne statistik. Det er et bevidst trade-off: opdateringen er hyppig nok til administrativ statistik, men undgår en konstant event-stream og en mere kompleks realtime-infrastruktur.

## Skalering og datavækst

Når databasen vokser, fortsætter dashboardet med at hente resultater gennem statistik-API'et og de valgte filtre/periode. Frontend henter aggregerede resultater frem for at gøre hele databasen til klient-state.

Hvis volumen senere gør endpointet dyrt, skal optimering ske på backend/databaseniveau — eksempelvis indeks, mere målrettede projections, pre-aggregation eller caching — uden at ændre den autoritative persistence-model. En sådan ændring skal måles og dokumenteres separat; dette dokument hævder ikke, at nuværende queries er ubegrænset skalerbare.

## Release-status og begrænsning

Denne dokumentation beskriver feature-branchen og PR #1083. På tidspunktet for denne verifikation var PR'en åben, mergeable og ikke merged til `main`. Grøn CI på feature-PR'en er repository-evidens for merge-readiness, men er ikke i sig selv bevis for en efterfølgende production deployment.

Hvis PR-head ændres, er tidligere exact-head CI ikke længere tilstrækkelig merge-evidens. Alle krævede gates skal være grønne igen for den nye head SHA før merge til `main`.
