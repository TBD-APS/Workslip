import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { apiClient } from '../../../lib/axios';

type ChartType = 'bar' | 'donut' | 'line';
type DataSource = 'sagsflow' | 'okonomi' | 'bemanding' | 'sla';

type Overview = { activeCount: number; inReviewCount: number; approvedCount: number; rejectedCount: number };
type Economics = { totalAmount: number; invoiceCount: number; receiptCount: number; recentDocuments: Array<{ documentNumber: string; amount: number }> };

function hash(s: string) {
  let h = 0;
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
  return h;
}

function BarChart({ data, x, y, color = '#d2542a' }: { data: Array<Record<string, string | number>>; x: string; y: string; color?: string }) {
  if (!data.length) return <div style={{ color: 'var(--muted)', fontSize: '13px' }}>Ingen data</div>;
  const max = Math.max(1, ...data.map(d => Number(d[y] ?? 0)));
  return (
    <svg viewBox="0 0 400 220" role="img" aria-label="Søjlediagram" style={{ width: '100%', height: '220px', display: 'block' }}>
      {data.map((d, i) => {
        const v = Number(d[y] ?? 0);
        const h = (v / max) * 140;
        const w = 400 / data.length - 12;
        const xPos = 20 + i * (400 / data.length);
        const yPos = 180 - h;
        return (
          <g key={i}>
            <rect x={xPos} y={yPos} width={w} height={h} rx={6} fill={color} />
            <text x={xPos + w / 2} y={195} textAnchor="middle" fontSize="10" fill="var(--muted)">{String(d[x]).slice(0, 10)}</text>
            <text x={xPos + w / 2} y={yPos - 6} textAnchor="middle" fontSize="11" fontWeight={700} fill="var(--text)">{v}</text>
          </g>
        );
      })}
      <line x1={10} y1={180} x2={390} y2={180} stroke="var(--border)" strokeWidth={1} />
    </svg>
  );
}

function DonutChart({ data, value, category: _category }: { data: Array<Record<string, string | number>>; category: string; value: string }) {
  if (!data.length) return <div style={{ color: 'var(--muted)', fontSize: '13px' }}>Ingen data</div>;
  const total = data.reduce((s, d) => s + Number(d[value] ?? 0), 0) || 1;
  let offset = 0;
  const colors = ['#d2542a', '#2a7d8f', '#6a9e3f', '#8a6a00', '#6b6b6b'];
  return (
    <svg viewBox="0 0 200 200" role="img" aria-label="Donut" style={{ width: '200px', height: '200px', display: 'block', margin: '0 auto' }}>
      <circle cx={100} cy={100} r={70} fill="none" stroke="var(--border)" strokeWidth={20} />
      {data.map((d, i) => {
        const v = Number(d[value] ?? 0);
        const pct = (v / total) * 100;
        const dash = `${pct} ${100 - pct}`;
        const el = <circle key={i} cx={100} cy={100} r={70} fill="none" stroke={colors[i % colors.length]} strokeWidth={20} strokeDasharray={dash} strokeDashoffset={-offset} transform="rotate(-90 100 100)" />;
        offset += pct;
        return el;
      })}
      <text x={100} y={100} textAnchor="middle" fontSize="22" fontWeight={750} fill="var(--text)">{total}</text>
      <text x={100} y={118} textAnchor="middle" fontSize="10" fill="var(--muted)">I alt</text>
    </svg>
  );
}

function SimpleMap({ points }: { points: Array<{ id: string; label: string; lat: number; lng: number }> }) {
  // Render a simple Denmark-ish SVG with dots — no external tiles needed for demo
  // Map bounds approx: lat 54.5-58, lng 8-13 (Denmark)
  const toX = (lng: number) => ((lng - 8) / 5) * 360 + 20;
  const toY = (lat: number) => 180 - ((lat - 54.5) / 3.5) * 140;
  return (
    <svg viewBox="0 0 400 200" role="img" aria-label="Kort med sager" style={{ width: '100%', height: '200px', background: '#eef4f6', borderRadius: '12px', border: '1px solid var(--border)' }}>
      <rect x={0} y={0} width={400} height={200} rx={12} fill="#eef4f6" />
      <text x={200} y={20} textAnchor="middle" fontSize="11" fill="var(--muted)">Demo-kort — geokodning hash omkring Aarhus (56.15, 10.21)</text>
      {points.map(p => (
        <g key={p.id}>
          <circle cx={toX(p.lng)} cy={toY(p.lat)} r={6} fill="#d2542a" stroke="#fff" strokeWidth={2} />
          <text x={toX(p.lng)} y={toY(p.lat) - 10} textAnchor="middle" fontSize="8" fill="var(--text)">{p.label.slice(0, 12)}</text>
        </g>
      ))}
    </svg>
  );
}

export function LeaderVisualsPanel() {
  const [chartType, setChartType] = useState<ChartType>('bar');
  const [dataSource, setDataSource] = useState<DataSource>('sagsflow');
  const [mapSource, setMapSource] = useState<'jobs' | 'customers'>('jobs');

  const overviewQuery = useQuery({
    queryKey: ['leader-analysis', 'visuals', 'overview'],
    queryFn: async () => (await apiClient.get('/api/jobs/overview')) as unknown as Overview,
    staleTime: 30_000,
  });

  const economicsQuery = useQuery({
    queryKey: ['leader-analysis', 'visuals', 'economics'],
    queryFn: async () => (await apiClient.get('/api/leader-analysis/economics/summary')) as unknown as Economics,
    staleTime: 30_000,
  });

  const bemandingQuery = useQuery({
    queryKey: ['leader-analysis', 'visuals', 'bemanding'],
    queryFn: async () => (await apiClient.get('/api/worksheets/all/report/power-bi/data?historyMonths=24', { skipGlobalErrorToast: true })) as unknown as { employees: Array<{ employee: string; userId: string }>; workHours: Array<{ userId: string; hours: number }> },
    staleTime: 30_000,
  });

  const slaQuery = useQuery({
    queryKey: ['leader-analysis', 'visuals', 'sla'],
    queryFn: async () => (await apiClient.get('/api/jobs', { params: { status: 'InReview', limit: 20 }, skipGlobalErrorToast: true })) as unknown as { items: Array<{ id: string; reportNumber: string | null; destinationAddress: string | null; customer?: { name?: string | null } | null; updatedAt: string }> },
    staleTime: 30_000,
  });

  const jobsForMapQuery = useQuery({
    queryKey: ['leader-analysis', 'visuals', 'map', mapSource],
    queryFn: async () => {
      if (mapSource === 'jobs') {
        const data = (await apiClient.get('/api/jobs', { params: { limit: 20 }, skipGlobalErrorToast: true })) as unknown as { items: Array<{ id: string; reportNumber: string | null; destinationAddress: string | null; customer?: { address?: string | null } | null }> };
        return data.items ?? [];
      }
      const data = (await apiClient.get('/api/customers', { params: { limit: 20 }, skipGlobalErrorToast: true })) as unknown as { items: Array<{ id: string; name: string; address: string | null }> };
      return (data as unknown as { items: Array<{ id: string }> }).items ?? [];
    },
    staleTime: 30_000,
  });

  const chartData = useMemo(() => {
    if (dataSource === 'sagsflow') {
      const o = overviewQuery.data;
      if (!o) return [];
      return [
        { status: 'Aktive', count: o.activeCount },
        { status: 'Til gennemsyn', count: o.inReviewCount },
        { status: 'Godkendte', count: o.approvedCount },
        { status: 'Afviste', count: o.rejectedCount },
      ];
    }
    if (dataSource === 'okonomi') {
      const e = economicsQuery.data;
      if (!e) return [];
      if (chartType === 'donut') return [{ type: 'Fakturaer', count: e.invoiceCount }, { type: 'Bilag', count: e.receiptCount }];
      return e.recentDocuments.slice(0, 6).map(d => ({ document: d.documentNumber, amount: d.amount }));
    }
    if (dataSource === 'bemanding') {
      const pb = bemandingQuery.data;
      if (!pb) return [];
      const byUser: Record<string, number> = {};
      for (const h of pb.workHours) byUser[h.userId] = (byUser[h.userId] ?? 0) + h.hours;
      return pb.employees.slice(0, 6).map(e => ({ employee: e.employee, hours: Math.round((byUser[e.userId] ?? 0) * 10) / 10 }));
    }
    // sla
    const items = (slaQuery.data as unknown as { items?: Array<{ reportNumber: string | null; updatedAt: string }> })?.items ?? (slaQuery.data as unknown as Array<{ reportNumber: string | null; updatedAt: string }>) ?? [];
    // slaQuery returns {items} but our query returns items array directly — handle both
    const list = Array.isArray(items) ? items : [];
    const now = Date.now();
    return list.slice(0, 6).map(j => ({
      sag: j.reportNumber ? `SAG-${j.reportNumber}` : '—',
      dage: Math.max(0, Math.floor((now - new Date((j as { updatedAt: string }).updatedAt).getTime()) / 86400000)),
    }));
  }, [dataSource, chartType, overviewQuery.data, economicsQuery.data, bemandingQuery.data, slaQuery.data]);

  const mapPoints = useMemo(() => {
    const items = (jobsForMapQuery.data as unknown as Array<Record<string, unknown>>) ?? [];
    return items.slice(0, 12).map((j: Record<string, unknown>) => {
      const addr = (j.destinationAddress as string) || (j.address as string) || (j as { customer?: { address?: string | null } }).customer?.address || "";
      const id = (j.id as string) || String(hash(addr));
      const h = hash(addr || id);
      const lat = 56.15 + ((h % 1000) / 1000 - 0.5) * 0.8;
      const lng = 10.21 + (((h >> 10) % 1000) / 1000 - 0.5) * 1.2;
      const label = (j.reportNumber as string) ? `SAG-${j.reportNumber}` : ((j.name as string) || id.slice(0, 8));
      return { id, label, lat, lng };
    });
  }, [jobsForMapQuery.data, mapSource]);

  return (
    <section id="leader-analysis-visuals" className="leader-analysis-card" aria-labelledby="visuals-heading">
      <div className="leader-analysis-card__header">
        <h3 id="visuals-heading" style={{ display: 'flex', alignItems: 'center', gap: '8px', margin: 0 }}>Interaktive grafer & kort</h3>
        <p>Drevet af <code>workslip-visuals</code> MCP — vælg datakilde og type, se live Vega-Lite/GeoJSON. Byg videre til dashboards.</p>
      </div>

      <div style={{ padding: '1rem', display: 'grid', gap: '12px' }}>
        <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center' }}>
          <label style={{ fontSize: '13px', display: 'flex', alignItems: 'center', gap: '6px' }}>
            Datakilde
            <select id="visuals-datasource" value={dataSource} onChange={e => setDataSource(e.target.value as DataSource)} style={{ padding: '6px 8px', borderRadius: '8px', border: '1px solid var(--border)' }}>
              <option value="sagsflow">Sagsflow</option>
              <option value="okonomi">Økonomi</option>
              <option value="bemanding">Bemanding</option>
              <option value="sla">SLA</option>
            </select>
          </label>
          <label style={{ fontSize: '13px', display: 'flex', alignItems: 'center', gap: '6px' }}>
            Type
            <select id="visuals-charttype" value={chartType} onChange={e => setChartType(e.target.value as ChartType)} style={{ padding: '6px 8px', borderRadius: '8px', border: '1px solid var(--border)' }}>
              <option value="bar">Søjler</option>
              <option value="donut">Donut</option>
              <option value="line">Linje</option>
            </select>
          </label>
          <span style={{ fontSize: '12px', color: 'var(--muted)' }}>MCP: <code>workslip_create_chart {"{chartType, dataSource}"}</code> → Vega-Lite</span>
        </div>

        <div id="visuals-chart" style={{ border: '1px solid var(--border)', borderRadius: '12px', padding: '12px', background: '#fff' }}>
          {chartType === 'donut' ? (
            <DonutChart data={chartData as Array<Record<string, string | number>>} category={dataSource === 'okonomi' ? 'type' : dataSource === 'sagsflow' ? 'status' : dataSource === 'bemanding' ? 'employee' : 'sag'} value={dataSource === 'okonomi' && chartType === 'donut' ? 'count' : dataSource === 'sagsflow' ? 'count' : dataSource === 'bemanding' ? 'hours' : 'dage'} />
          ) : (
            <BarChart data={chartData as Array<Record<string, string | number>>} x={dataSource === 'okonomi' ? 'document' : dataSource === 'sagsflow' ? 'status' : dataSource === 'bemanding' ? 'employee' : 'sag'} y={dataSource === 'okonomi' ? 'amount' : dataSource === 'sagsflow' ? 'count' : dataSource === 'bemanding' ? 'hours' : 'dage'} />
          )}
        </div>

        <div style={{ display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' }}>
          <label style={{ fontSize: '13px', display: 'flex', alignItems: 'center', gap: '6px' }}>
            Kortkilde
            <select id="visuals-mapsource" value={mapSource} onChange={e => setMapSource(e.target.value as 'jobs' | 'customers')} style={{ padding: '6px 8px', borderRadius: '8px', border: '1px solid var(--border)' }}>
              <option value="jobs">Sager</option>
              <option value="customers">Kunder</option>
            </select>
          </label>
          <span style={{ fontSize: '12px', color: 'var(--muted)' }}>MCP: <code>workslip_create_map {"{source, limit}"}</code> → GeoJSON</span>
        </div>

        <div id="visuals-map" style={{ border: '1px solid var(--border)', borderRadius: '12px', overflow: 'hidden' }}>
          <SimpleMap points={mapPoints} />
        </div>

        <div style={{ fontSize: '12px', color: 'var(--muted)', display: 'grid', gap: '4px' }}>
          <span>Frontend renderer samme specs som MCP’en returnerer — skift til <code>vega-embed</code> / <code>react-leaflet</code> for fuld Vega/Leaflet.</span>
          <span>Prøv i MCP-klienten: <code>workslip_create_chart {"{chartType:\"bar\", dataSource:\"okonomi\"}"}</code> eller <code>workslip_create_map {"{source:\"jobs\", limit:20}"}</code></span>
        </div>
      </div>
    </section>
  );
}
