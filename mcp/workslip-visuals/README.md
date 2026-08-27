# Workslip Visuals MCP

**Grafer, maps og dashboards for Lederanalyse — user engagement**

MCP-server der gør Workslip-data til visualiseringer uden at hardcode chart-logik i frontend. Returnerer **Vega-Lite** specs (grafer) og **GeoJSON + Leaflet** specs (kort) som enhver MCP-klient (opencode, Claude, Workslip frontend) kan rendere.

## Hvad den gør

- **Læser Workslip som Admin** via `GET /api/jobs/overview`, `/api/leader-analysis/economics/summary`, `/api/worksheets/all/report/power-bi/data`, `/api/jobs`, `/api/customers`
- **Genererer grafer**: `workslip_create_chart` → Vega-Lite JSON (`bar`, `donut`, `line`) for `sagsflow` / `okonomi` / `bemanding` / `sla`
- **Genererer kort**: `workslip_create_map` → GeoJSON `FeatureCollection` + Leaflet spec (`center`, `zoom`, `tileLayer`, `geojson`) for `jobs`/`customers`
- **Dashboard**: `workslip_create_dashboard` → layout-JSON der mapper direkte til Lederanalyse-sektioner (`#leader-analysis-powerbi`, `#leader-analysis-economics`, `#leader-analysis-bemanding`, `#leader-analysis-sla`, `#leader-analysis-export`)

## Tools

| Tool | Beskrivelse |
|---|---|
| `workslip_get_leader_analysis` | Rå Lederanalyse-nøgletal (samme som `/app/lederanalyse` KPI’er) |
| `workslip_get_economics` | Økonomi & bilag (provider, totalAmount, recentDocuments) — `startDate`/`endDate` optional |
| `workslip_get_engagement` | Bemanding + SLA rådata (Power BI + InReview-jobs) |
| `workslip_create_chart` | `chartType`: `bar`/`donut`/`line`, `dataSource`: `sagsflow`/`okonomi`/`bemanding`/`sla` → Vega-Lite spec |
| `workslip_create_map` | `source`: `jobs`/`customers`, `status?`, `limit?` (1-50) → GeoJSON + Leaflet spec |
| `workslip_create_dashboard` | `includeEconomics?`, `includeMap?` → dashboard layout-JSON |

## Kørsel lokalt

```bash
cd mcp/workslip-visuals
npm install
# Sæt dev-token (Admin) — hent via:
# curl -X POST -H "Content-Type: application/json" -d '{"email":"admin@17v3ygzs.mailosaur.net"}' http://127.0.0.1:5262/api/dev/token
export WORKSLIP_API_URL=http://127.0.0.1:5262
export WORKSLIP_API_TOKEN=<token>
export WORKSLIP_APP_URL=http://127.0.0.1:5270
npm run dev
# eller: npx tsx src/index.ts
```

`WORKSLIP_API_TOKEN` er påkrævet for Admin-endpoints (`/api/leader-analysis/*`, `/api/worksheets/...`). Uden token returnerer tools 401 med hjælpetekst.

## Opencode config

Tilføj til `opencode.json` (project root):

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "workslip-visuals": {
      "type": "local",
      "command": ["npx", "tsx", "mcp/workslip-visuals/src/index.ts"],
      "enabled": true,
      "environment": {
        "WORKSLIP_API_URL": "http://127.0.0.1:5262",
        "WORKSLIP_API_TOKEN": "{env:WORKSLIP_API_TOKEN}",
        "WORKSLIP_APP_URL": "http://127.0.0.1:5270"
      }
    }
  }
}
```

Genstart opencode efter ændring.

## Frontend rendering (Workslip Lederanalyse)

- **Graf**: `npm add vega vega-lite vega-embed` → `embed(container, spec)` — spec fra `workslip_create_chart` er klar til `vegaEmbed`.
- **Kort**: `npm add leaflet react-leaflet` → `<MapContainer center={spec.center} zoom={spec.zoom}><TileLayer url={spec.tileLayer} /><GeoJSON data={spec.geojson} /></MapContainer>`
- **Dashboard**: map `layout.sections[].id` til eksisterende DOM IDs i `src/FE/src/features/leader-analysis/routes/Lederanalyse.tsx:319` (`leader-analysis-powerbi`, `leader-analysis-economics`, `leader-analysis-bemanding`, `leader-analysis-sla`, `leader-analysis-export`).

## Geokodning

Demo-geokodning er deterministisk hash omkring Aarhus (56.15, 10.21) for at undgå ekstern API-afhængighed. Skift til Nominatim/Mapbox ved at erstatte `hash()` i `workslip_create_map` med rigtig geokodning af `destinationAddress`/`customer.address`.

## Sikkerhed

- Kræver Admin-token — samme `RequireAdmin` som Lederanalyse (`src/BE/WorkslipApi/Endpoints/LeaderAnalysisEndpoints.cs:9`).
- Ingen secrets committes — brug `{env:WORKSLIP_API_TOKEN}` i `opencode.json`, ikke hardcodet token.

## Næste skridt for engagement

- **Gamification**: udvid `workslip_get_engagement` med streaks/badges pr. medarbejder (timer thresholds)
- **Realtime**: abonner på `GET /api/worksheets/all/report/power-bi/data` med 30s poll (som Lederanalyse) eller websocket
- **Deling**: `workslip_create_dashboard` → eksporter som PDF/CSV via eksisterende `#leader-analysis-export-csv` / `#leader-analysis-export-pdf` handlers
