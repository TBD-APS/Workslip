#!/usr/bin/env node
/**
 * Workslip Visuals MCP — grafer, maps og dashboards for Lederanalyse
 *
 * Exposes Workslip data as MCP tools that return Vega-Lite specs and GeoJSON
 * so any MCP client (opencode, Claude, Workslip frontend) can render
 * user-engagement visuals without hard-coding chart logic.
 *
 * Env:
 *   WORKSLIP_API_URL   - default http://127.0.0.1:5262
 *   WORKSLIP_API_TOKEN - dev token (or superadmin@... via /api/dev/token)
 *   WORKSLIP_APP_URL   - default http://127.0.0.1:5270
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";

const API_URL = process.env.WORKSLIP_API_URL?.replace(/\/$/, "") || "http://127.0.0.1:5262";
const API_TOKEN = process.env.WORKSLIP_API_TOKEN || "";
const APP_URL = process.env.WORKSLIP_APP_URL || "http://127.0.0.1:5270";

// ---------------------------------------------------------------------------
// Helpers: Workslip API fetch with tenant isolation (uses dev token if provided)
// ---------------------------------------------------------------------------
async function workslipFetch(path: string, init: RequestInit = {}) {
  const headers: Record<string, string> = {
    Accept: "application/json",
    ...(init.headers as Record<string, string> | undefined),
  };
  if (API_TOKEN) headers.Authorization = `Bearer ${API_TOKEN}`;

  const url = `${API_URL}${path}`;
  const res = await fetch(url, { ...init, headers });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(`Workslip API ${res.status} ${res.statusText} for ${path}: ${text.slice(0, 500)}`);
  }
  const ct = res.headers.get("content-type") || "";
  if (ct.includes("application/json")) return res.json();
  return res.text();
}

async function getLeaderAnalysis() {
  return workslipFetch("/api/jobs/overview");
}

async function getEconomicsSummary(startDate?: string, endDate?: string) {
  const qs = new URLSearchParams();
  if (startDate) qs.set("startDate", startDate);
  if (endDate) qs.set("endDate", endDate);
  const q = qs.toString() ? `?${qs}` : "";
  return workslipFetch(`/api/leader-analysis/economics/summary${q}`);
}

async function getJobs(status?: string, limit = 20) {
  const qs = new URLSearchParams();
  if (status) qs.set("status", status);
  qs.set("limit", String(limit));
  return workslipFetch(`/api/jobs?${qs}`);
}

async function getPowerBiData() {
  return workslipFetch("/api/worksheets/all/report/power-bi/data?historyMonths=24");
}

// ---------------------------------------------------------------------------
// Vega-Lite helpers
// ---------------------------------------------------------------------------
type VegaLiteSpec = Record<string, unknown>;

function vegaBar(data: unknown[], x: string, y: string, title: string): VegaLiteSpec {
  return {
    $schema: "https://vega.github.io/schema/vega-lite/v5.json",
    title,
    data: { values: data },
    mark: "bar",
    encoding: {
      x: { field: x, type: "nominal", axis: { labelAngle: -30 } },
      y: { field: y, type: "quantitative" },
      color: { field: x, type: "nominal", legend: null },
      tooltip: [{ field: x }, { field: y }],
    },
    width: "container",
    height: 220,
  };
}

function vegaDonut(data: unknown[], category: string, value: string, title: string): VegaLiteSpec {
  return {
    $schema: "https://vega.github.io/schema/vega-lite/v5.json",
    title,
    data: { values: data },
    mark: { type: "arc", innerRadius: 60 },
    encoding: {
      theta: { field: value, type: "quantitative" },
      color: { field: category, type: "nominal" },
      tooltip: [{ field: category }, { field: value }],
    },
    width: "container",
    height: 240,
  };
}

function vegaLine(data: unknown[], x: string, y: string, title: string): VegaLiteSpec {
  return {
    $schema: "https://vega.github.io/schema/vega-lite/v5.json",
    title,
    data: { values: data },
    mark: { type: "line", point: true },
    encoding: {
      x: { field: x, type: "temporal" },
      y: { field: y, type: "quantitative" },
      tooltip: [{ field: x }, { field: y }],
    },
    width: "container",
    height: 220,
  };
}

// ---------------------------------------------------------------------------
// MCP Server
// ---------------------------------------------------------------------------
const server = new McpServer({
  name: "workslip-visuals",
  version: "0.1.0",
});

// Tool: get_leader_analysis — raw data for custom visuals
server.tool(
  "workslip_get_leader_analysis",
  "Hent Lederanalyse-nøgletal (samme som /app/lederanalyse). Bruges som kilde til grafer. Kræver WORKSLIP_API_TOKEN med Admin.",
  {},
  async () => {
    const data = await getLeaderAnalysis();
    return {
      content: [{ type: "text", text: JSON.stringify(data, null, 2) }],
    };
  }
);

// Tool: get_economics — penge & bilag
server.tool(
  "workslip_get_economics",
  "Hent økonomi & bilag fra e-conomic/Mock (samme som Lederanalyse → Økonomi & bilag). Returnerer provider, totalAmount, antal, recentDocuments.",
  {
    startDate: z.string().optional().describe("YYYY-MM-DD, default sidste 6 mdr."),
    endDate: z.string().optional().describe("YYYY-MM-DD, default i dag"),
  },
  async ({ startDate, endDate }) => {
    const data = await getEconomicsSummary(startDate, endDate);
    return { content: [{ type: "text", text: JSON.stringify(data, null, 2) }] };
  }
);

// Tool: get_engagement — bemanding, sagsøkonomi, SLA-rådata
server.tool(
  "workslip_get_engagement",
  "Hent rådata til bruger-engagement: bemanding (Power BI employees/workHours), sagsøkonomi og InReview-jobs til SLA.",
  {},
  async () => {
    const [powerBi, inReview] = await Promise.all([
      getPowerBiData().catch((e) => ({ error: String(e) })),
      getJobs("InReview", 20).catch((e) => ({ error: String(e) })),
    ]);
    return {
      content: [{ type: "text", text: JSON.stringify({ powerBi, inReview }, null, 2) }],
    };
  }
);

// Tool: create_chart — Vega-Lite spec from Workslip data
server.tool(
  "workslip_create_chart",
  "Lav en Vega-Lite graf-spec ud fra Workslip-data. Vælg type og datakilde — returnerer en Vega-Lite JSON der kan renders i Workslip frontend (vega-embed) eller i MCP klienten.",
  {
    chartType: z.enum(["bar", "donut", "line"]).describe("bar = søjler, donut = donut, line = linje/tid"),
    dataSource: z.enum(["sagsflow", "okonomi", "bemanding", "sla"]).describe("Hvilken Lederanalyse-kilde: sagsflow = statusfordeling, okonomi = faktura/bilag, bemanding = timer pr. medarbejder, sla = dage i gennemsyn"),
    title: z.string().optional().describe("Graf-titel, default autogenereres"),
  },
  async ({ chartType, dataSource, title }) => {
    let spec: VegaLiteSpec;
    let dataPreview: unknown[] = [];

    if (dataSource === "sagsflow") {
      const overview: { activeCount: number; inReviewCount: number; approvedCount: number; rejectedCount: number } = await getLeaderAnalysis();
      dataPreview = [
        { status: "Aktive", count: overview.activeCount },
        { status: "Til gennemsyn", count: overview.inReviewCount },
        { status: "Godkendte", count: overview.approvedCount },
        { status: "Afviste", count: overview.rejectedCount },
      ];
      spec = chartType === "donut" ? vegaDonut(dataPreview, "status", "count", title || "Sagsflow — statusfordeling") : vegaBar(dataPreview, "status", "count", title || "Sagsflow");
    } else if (dataSource === "okonomi") {
      const eco = (await getEconomicsSummary()) as { totalAmount: number; invoiceCount: number; receiptCount: number; recentDocuments: Array<{ documentNumber: string; amount: number }> };
      if (chartType === "donut") {
        dataPreview = [
          { type: "Fakturaer", count: eco.invoiceCount },
          { type: "Bilag", count: eco.receiptCount },
        ];
        spec = vegaDonut(dataPreview, "type", "count", title || "Økonomi — faktura vs. bilag");
      } else {
        dataPreview = eco.recentDocuments.slice(0, 8).map((d) => ({ document: d.documentNumber, amount: d.amount }));
        spec = vegaBar(dataPreview, "document", "amount", title || "Økonomi — beløb pr. bilag");
      }
    } else if (dataSource === "bemanding") {
      const pb = (await getPowerBiData()) as { employees: Array<{ employee: string; userId: string }>; workHours: Array<{ userId: string; hours: number }> };
      const hoursByUser: Record<string, number> = {};
      for (const h of pb.workHours) hoursByUser[h.userId] = (hoursByUser[h.userId] ?? 0) + h.hours;
      dataPreview = pb.employees.slice(0, 8).map((e) => ({ employee: e.employee, hours: Math.round((hoursByUser[e.userId] ?? 0) * 10) / 10 }));
      spec = vegaBar(dataPreview, "employee", "hours", title || "Bemanding — timer pr. medarbejder");
    } else {
      // sla
      const inReview = (await getJobs("InReview", 20)) as { items: Array<{ reportNumber: string | null; updatedAt: string }> };
      const now = Date.now();
      dataPreview = (inReview.items || []).slice(0, 8).map((j) => ({
        sag: j.reportNumber ? `SAG-${j.reportNumber}` : j.reportNumber || "—",
        dage: Math.max(0, Math.floor((now - new Date(j.updatedAt).getTime()) / 86400000)),
      }));
      spec = chartType === "line" ? vegaLine(dataPreview, "sag", "dage", title || "SLA — dage i gennemsyn") : vegaBar(dataPreview, "sag", "dage", title || "SLA — dage i gennemsyn");
    }

    return {
      content: [
        { type: "text", text: `Vega-Lite spec for ${chartType} / ${dataSource}:\n\`\`\`json\n${JSON.stringify(spec, null, 2)}\n\`\`\`\n\nData preview (${dataPreview.length} rækker):\n${JSON.stringify(dataPreview.slice(0, 5), null, 2)}` },
      ],
    };
  }
);

// Tool: create_map — GeoJSON with job/customer markers
server.tool(
  "workslip_create_map",
  "Lav et kort (GeoJSON + Leaflet-spec) med sager/kunder. Returnerer GeoJSON FeatureCollection der kan renders med Leaflet/Mapbox i Workslip. Adresser geokodes ikke her — bruger destinationAddress/customer.address som label; klienten kan geokode via Nominatim.",
  {
    source: z.enum(["jobs", "customers"]).describe("jobs = sager med destinationAddress, customers = kunder med address"),
    status: z.string().optional().describe("For jobs: filtrer på status (Draft, InReview, Approved, Rejected) — ellers alle"),
    limit: z.number().min(1).max(50).optional().describe("Max markører, default 20"),
  },
  async ({ source, status, limit = 20 }) => {
    let features: Array<{ type: string; geometry: { type: string; coordinates: [number, number] }; properties: Record<string, unknown> }> = [];
    let items: Array<Record<string, unknown>> = [];

    if (source === "jobs") {
      const qs = new URLSearchParams();
      if (status) qs.set("status", status);
      qs.set("limit", String(limit));
      const data = (await workslipFetch(`/api/jobs?${qs}`)) as { items: Array<{ id: string; reportNumber: string | null; destinationAddress: string | null; customer?: { name?: string | null; address?: string | null } | null; status: string; updatedAt: string }> };
      items = (data.items || []) as unknown as Array<Record<string, unknown>>;
      // Use a deterministic fake geocode: hash address to lat/lng near Aarhus (56.15, 10.21) for demo
      const hash = (s: string) => {
        let h = 0;
        for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
        return h;
      };
      features = items
        .filter((j) => (j as { destinationAddress?: string | null }).destinationAddress || (j as { customer?: { address?: string | null } | null }).customer?.address)
        .slice(0, limit)
        .map((j) => {
          const addr = ((j as { destinationAddress?: string | null }).destinationAddress || (j as { customer?: { address?: string | null } | null }).customer?.address || "") as string;
          const h = hash(addr || (j as { id: string }).id);
          const lat = 56.15 + ((h % 1000) / 1000 - 0.5) * 0.8;
          const lng = 10.21 + (((h >> 10) % 1000) / 1000 - 0.5) * 1.2;
          return {
            type: "Feature",
            geometry: { type: "Point", coordinates: [lng, lat] as [number, number] },
            properties: {
              id: (j as { id: string }).id,
              reportNumber: (j as { reportNumber: string | null }).reportNumber,
              address: addr,
              status: (j as { status: string }).status,
              popup: `${(j as { reportNumber: string | null }).reportNumber ? `SAG-${(j as { reportNumber: string | null }).reportNumber}` : (j as { id: string }).id.slice(0, 8)} — ${addr}`,
            },
          };
        });
    } else {
      const data = (await workslipFetch(`/api/customers?limit=${limit}`)) as { items: Array<{ id: string; name: string; address: string | null }> };
      items = (data.items || []) as unknown as Array<Record<string, unknown>>;
      const hash = (s: string) => {
        let h = 0;
        for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
        return h;
      };
      features = items
        .slice(0, limit)
        .map((c) => {
          const addr = (c as { address: string | null }).address || "";
          const h = hash(addr || (c as { id: string }).id);
          const lat = 56.15 + ((h % 1000) / 1000 - 0.5) * 0.8;
          const lng = 10.21 + (((h >> 10) % 1000) / 1000 - 0.5) * 1.2;
          return {
            type: "Feature",
            geometry: { type: "Point", coordinates: [lng, lat] as [number, number] },
            properties: {
              id: (c as { id: string }).id,
              name: (c as { name: string }).name,
              address: addr,
              popup: `${(c as { name: string }).name} — ${addr}`,
            },
          };
        });
    }

    const geojson = { type: "FeatureCollection", features };
    const leafletSpec = {
      center: [56.15, 10.21],
      zoom: 7,
      tileLayer: "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png",
      geojson,
    };

    return {
      content: [
        {
          type: "text",
          text: `GeoJSON + Leaflet spec for ${source} (${features.length} markører):\n\`\`\`json\n${JSON.stringify(leafletSpec, null, 2)}\n\`\`\`\n\nTip: Render i Workslip med <MapContainer> + <GeoJSON data={spec.geojson} /> (react-leaflet) eller send til /app/lederanalyse → Kort-fane. Geokodning er deterministisk demo — skift til Nominatim/Mapbox for præcise koordinater.`,
        },
      ],
    };
  }
);

// Tool: create_dashboard — combined layout
server.tool(
  "workslip_create_dashboard",
  "Lav et komplet Lederanalyse-dashboard layout (Power BI + økonomi + bemanding + SLA + kort) som JSON der kan gemmes og renders i Workslip.",
  {
    includeEconomics: z.boolean().optional().describe("Inkludér økonomi & bilag (default true)"),
    includeMap: z.boolean().optional().describe("Inkludér kort (default false)"),
  },
  async ({ includeEconomics = true, includeMap = false }) => {
    const [overview, economics] = await Promise.all([
      getLeaderAnalysis().catch(() => null),
      includeEconomics ? getEconomicsSummary().catch(() => null) : Promise.resolve(null),
    ]);

    const layout = {
      title: "Lederanalyse — Dashboard",
      appUrl: `${APP_URL}/app/lederanalyse`,
      sections: [
        { id: "powerbi", title: "Power BI — virksomhedsstatistik", component: "AdminPowerBiJobStatusChart", source: "/api/worksheets/all/report/power-bi/data" },
        ...(includeEconomics && economics ? [{ id: "economics", title: "Økonomi & bilag", provider: (economics as { providerDisplayName: string }).providerDisplayName, totalAmount: (economics as { totalAmount: number }).totalAmount }] : []),
        { id: "kpi", title: "KPI — sagsflow", source: overview },
        { id: "bemanding", title: "Bemanding & belægning", source: "/api/worksheets/all/report/power-bi/data" },
        { id: "sla", title: "SLA — Til gennemsyn", source: "/api/jobs?status=InReview" },
        ...(includeMap ? [{ id: "map", title: "Kort — sager", spec: "use workslip_create_map with source=jobs" }] : []),
        { id: "export", title: "Eksport", actions: ["csv", "pdf"] },
      ],
      generatedAt: new Date().toISOString(),
    };

    return {
      content: [{ type: "text", text: `Dashboard layout:\n\`\`\`json\n${JSON.stringify(layout, null, 2)}\n\`\`\`\n\nRender i Workslip: map hver section.id til eksisterende Lederanalyse-komponenter (#leader-analysis-powerbi, #leader-analysis-economics, #leader-analysis-bemanding, #leader-analysis-sla, #leader-analysis-export). For kort: kald workslip_create_map separat og embed GeoJSON.` }],
    };
  }
);

// ---------------------------------------------------------------------------
// Start
// ---------------------------------------------------------------------------
async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error(`[workslip-visuals] MCP running — API ${API_URL} → App ${APP_URL} — tools: workslip_get_leader_analysis, workslip_get_economics, workslip_get_engagement, workslip_create_chart, workslip_create_map, workslip_create_dashboard`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
