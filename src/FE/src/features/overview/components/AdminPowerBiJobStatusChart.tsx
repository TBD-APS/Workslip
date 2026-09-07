import { useQuery } from '@tanstack/react-query';
import { BarChart3, Loader2, RefreshCw, UsersRound, WalletCards } from 'lucide-react';
import { useMemo, useState } from 'react';
import { apiClient } from '../../../lib/axios';
import { formatMonthYearShort } from '../../../lib/presentation/date';

type AnalyticsResponse = {
  generatedAtUtc: string;
  employees: Array<{ userId: string; employee: string; role: string }>;
  workHours: Array<{ userId: string; hours: number; billableAmount: number | null }>;
  jobs: Array<{ status: string }>;
  customers: Array<{ customerId: string; customer: string; createdDate: string }>;
};

type Segment = {
  key: 'draft' | 'inReview' | 'approved' | 'rejected' | 'other';
  label: string;
  count: number;
};

type ChartSegment = Segment & { percentage: number; offset: number };
type DashboardTab = 'cases' | 'employees' | 'customers';
const REFRESH_INTERVAL_MS = 30_000;

const fetchAnalytics = async () => (await apiClient.get(
  '/api/worksheets/all/report/power-bi/data?historyMonths=24',
  { skipGlobalErrorToast: true },
)) as AnalyticsResponse;

const buildSegments = (jobs: AnalyticsResponse['jobs']): Segment[] => {
  const counts = jobs.reduce<Record<string, number>>((acc, job) => {
    const key = job.status.toLowerCase();
    acc[key] = (acc[key] ?? 0) + 1;
    return acc;
  }, {});
  const known = (counts.draft ?? 0) + (counts.inreview ?? 0) + (counts.approved ?? 0) + (counts.rejected ?? 0);
  return [
    { key: 'draft', label: 'Aktive', count: counts.draft ?? 0 },
    { key: 'inReview', label: 'Til gennemsyn', count: counts.inreview ?? 0 },
    { key: 'approved', label: 'Godkendte', count: counts.approved ?? 0 },
    { key: 'rejected', label: 'Afviste', count: counts.rejected ?? 0 },
    { key: 'other', label: 'Øvrige', count: Math.max(0, jobs.length - known) },
  ].filter((segment) => segment.count > 0) as Segment[];
};

const buildChartSegments = (segments: Segment[], total: number): ChartSegment[] =>
  segments.map((segment, index) => {
    const percentage = total > 0 ? (segment.count / total) * 100 : 0;
    const offset = segments.slice(0, index).reduce(
      (sum, previous) => sum + (total > 0 ? (previous.count / total) * 100 : 0),
      0,
    );
    return { ...segment, percentage, offset };
  });

const formatCurrency = (value: number) => new Intl.NumberFormat('da-DK', {
  style: 'currency',
  currency: 'DKK',
  maximumFractionDigits: 0,
}).format(value);

const formatPercent = (value: number) => new Intl.NumberFormat('da-DK', {
  style: 'percent',
  maximumFractionDigits: 0,
}).format(value);

export const calculateEconomicsKpis = (rows: AnalyticsResponse['workHours']) => {
  const totalHours = rows.reduce((sum, row) => sum + (row.hours ?? 0), 0);
  const pricedHours = rows.reduce((sum, row) => sum + (row.billableAmount !== null ? (row.hours ?? 0) : 0), 0);
  const totalBillable = rows.reduce((sum, row) => sum + (row.billableAmount ?? 0), 0);
  const unpricedHours = Math.max(0, totalHours - pricedHours);
  const pricingCoverage = totalHours > 0 ? pricedHours / totalHours : 0;
  const averageBillableRate = pricedHours > 0 ? totalBillable / pricedHours : 0;

  return { totalHours, pricedHours, unpricedHours, totalBillable, pricingCoverage, averageBillableRate };
};

const monthKey = (date: Date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
const monthLabel = (key: string) => {
  const [year, month] = key.split('-').map(Number);
  return formatMonthYearShort(new Date(year, month - 1, 1)) ?? key;
};

export function AdminPowerBiJobStatusChart() {
  const [tab, setTab] = useState<DashboardTab>('cases');
  const analyticsQuery = useQuery({
    queryKey: ['power-bi', 'overview', 'analytics'],
    queryFn: fetchAnalytics,
    retry: false,
    staleTime: 15_000,
    refetchInterval: REFRESH_INTERVAL_MS,
    refetchOnWindowFocus: true,
  });

  const data = analyticsQuery.data;
  const segments = useMemo(() => buildSegments(data?.jobs ?? []), [data?.jobs]);
  const total = data?.jobs.length ?? 0;
  const chartSegments = buildChartSegments(segments, total);
  const economics = useMemo(() => calculateEconomicsKpis(data?.workHours ?? []), [data?.workHours]);

  const employeeValues = useMemo(() => {
    if (!data) return [];
    const values = data.workHours.reduce<Record<string, { amount: number; hours: number; pricedHours: number }>>((acc, row) => {
      const current = acc[row.userId] ?? { amount: 0, hours: 0, pricedHours: 0 };
      current.amount += row.billableAmount ?? 0;
      current.hours += row.hours ?? 0;
      if (row.billableAmount !== null) current.pricedHours += row.hours ?? 0;
      acc[row.userId] = current;
      return acc;
    }, {});
    return data.employees
      .map((employee) => {
        const value = values[employee.userId] ?? { amount: 0, hours: 0, pricedHours: 0 };
        return {
          ...employee,
          ...value,
          effectiveRate: value.pricedHours > 0 ? value.amount / value.pricedHours : 0,
        };
      })
      .filter((employee) => employee.hours > 0)
      .sort((a, b) => b.amount - a.amount)
      .slice(0, 8);
  }, [data]);

  const customerGrowth = useMemo(() => {
    if (!data) return [];
    const now = new Date();
    const keys = Array.from({ length: 6 }, (_, index) => {
      const date = new Date(now.getFullYear(), now.getMonth() - (5 - index), 1);
      return monthKey(date);
    });
    const counts = data.customers.reduce<Record<string, number>>((acc, customer) => {
      const date = new Date(customer.createdDate);
      if (!Number.isNaN(date.getTime())) {
        const key = monthKey(date);
        acc[key] = (acc[key] ?? 0) + 1;
      }
      return acc;
    }, {});
    return keys.map((key) => ({ key, label: monthLabel(key), count: counts[key] ?? 0 }));
  }, [data]);

  const maxCustomerCount = Math.max(1, ...customerGrowth.map((item) => item.count));

  return (
    <section id="admin-power-bi-job-status" className="overview-power-bi-card" aria-labelledby="overview-power-bi-heading" data-testid="admin-power-bi-job-status">
      <div className="overview-section-header overview-power-bi-header">
        <div>
          <h3 id="overview-power-bi-heading">Virksomhedsstatistik</h3>
          <p>Opdateres automatisk hvert 30. sekund.</p>
        </div>
        {analyticsQuery.isError && (
          <button id="overview-power-bi-retry" type="button" className="btn btn-secondary overview-power-bi-retry" onClick={() => { void analyticsQuery.refetch(); }}>
            <RefreshCw size={16} aria-hidden="true" /> Prøv igen
          </button>
        )}
      </div>

      <div id="overview-analytics-tabs" className="overview-analytics-tabs" role="tablist" aria-label="Virksomhedsstatistik">
        <button id="overview-analytics-tab-cases" type="button" role="tab" aria-selected={tab === 'cases'} onClick={() => setTab('cases')}><BarChart3 size={16} aria-hidden="true" /> Sager</button>
        <button id="overview-analytics-tab-employees" type="button" role="tab" aria-selected={tab === 'employees'} onClick={() => setTab('employees')}><WalletCards size={16} aria-hidden="true" /> Økonomi</button>
        <button id="overview-analytics-tab-customers" type="button" role="tab" aria-selected={tab === 'customers'} onClick={() => setTab('customers')}><UsersRound size={16} aria-hidden="true" /> Nye kunder</button>
      </div>

      {analyticsQuery.isPending ? (
        <div id="overview-analytics-loading" className="overview-power-bi-state" role="status"><Loader2 className="overview-power-bi-spinner" size={22} aria-hidden="true" /><span>Henter rapportdata…</span></div>
      ) : analyticsQuery.isError ? (
        <div id="overview-analytics-error" className="overview-power-bi-state overview-power-bi-state--error" role="alert"><strong>Kunne ikke hente statistik</strong><span>Resten af overblikssiden virker fortsat.</span></div>
      ) : tab === 'cases' ? (
        <div id="overview-analytics-panel-cases" className="overview-power-bi-content">
          <div className="overview-power-bi-donut-wrap">
            <svg className="overview-power-bi-donut" viewBox="0 0 42 42" role="img" aria-label={`Sagsfordeling. ${segments.map((segment) => `${segment.label}: ${segment.count}`).join(', ') || 'Ingen sager'}`}>
              <circle className="overview-power-bi-donut__track" cx="21" cy="21" r="15.9155" />
              {chartSegments.map((segment) => <circle key={segment.key} className={`overview-power-bi-donut__segment overview-power-bi-donut__segment--${segment.key}`} cx="21" cy="21" r="15.9155" strokeDasharray={`${segment.percentage} ${100 - segment.percentage}`} strokeDashoffset={-segment.offset} />)}
            </svg>
            <div className="overview-power-bi-donut__center" aria-hidden="true"><strong>{total}</strong><span>I alt</span></div>
          </div>
          <ul className="overview-power-bi-legend" aria-label="Fordeling af sager">
            {segments.length ? segments.map((segment) => <li key={segment.key}><span className={`overview-power-bi-legend__dot overview-power-bi-legend__dot--${segment.key}`} aria-hidden="true" /><span>{segment.label}</span><strong>{segment.count} ({Math.round((segment.count / Math.max(1, total)) * 100)}%)</strong></li>) : <li className="overview-power-bi-legend__empty">Der er ingen sager at vise endnu.</li>}
          </ul>
        </div>
      ) : tab === 'employees' ? (
        <div id="overview-analytics-panel-employees" role="tabpanel" style={{ display: 'grid', gap: '14px' }}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))', gap: '10px' }}>
            <div className="leader-kpi-card" id="overview-economics-billable-value">
              <span className="leader-kpi-card__label">Fakturerbar værdi</span>
              <strong className="leader-kpi-card__value">{formatCurrency(economics.totalBillable)}</strong>
              <span className="leader-kpi-card__hint">Fra registrerede timer</span>
            </div>
            <div className="leader-kpi-card" id="overview-economics-total-hours">
              <span className="leader-kpi-card__label">Registrerede timer</span>
              <strong className="leader-kpi-card__value">{economics.totalHours.toFixed(1)} t</strong>
              <span className="leader-kpi-card__hint">Seneste 24 måneder</span>
            </div>
            <div className="leader-kpi-card" id="overview-economics-pricing-coverage">
              <span className="leader-kpi-card__label">Prisdækning</span>
              <strong className="leader-kpi-card__value">{formatPercent(economics.pricingCoverage)}</strong>
              <span className="leader-kpi-card__hint">Timer med fakturerbar sats</span>
            </div>
            <div className="leader-kpi-card" id="overview-economics-effective-rate">
              <span className="leader-kpi-card__label">Effektiv sats</span>
              <strong className="leader-kpi-card__value">{formatCurrency(economics.averageBillableRate)}/t</strong>
              <span className="leader-kpi-card__hint">Værdi / prissatte timer</span>
            </div>
          </div>

          {economics.unpricedHours > 0 && (
            <div id="overview-economics-data-warning" role="status" style={{ border: '1px solid var(--status-amber-text)', borderRadius: '12px', padding: '10px 12px', background: 'color-mix(in srgb, var(--status-amber-text) 8%, var(--surface))', fontSize: '13px' }}>
              <strong>{economics.unpricedHours.toFixed(1)} timer mangler fakturerbar sats.</strong>{' '}
              De timer tæller med i tidsforbruget, men ikke i fakturerbar værdi. Sæt satsen på medarbejderprofilen for et komplet økonomibillede.
            </div>
          )}

          <p className="overview-analytics-note" style={{ margin: 0 }}>
            Fakturerbar værdi er et omsætningsmål — ikke dækningsbidrag. Workslip har endnu ikke en verificeret intern lønkostpris eller fuld materialekost pr. sag, så vi viser ikke en kunstig profitmargin.
          </p>

          <div className="overview-analytics-list">
            <div style={{ display: 'flex', justifyContent: 'space-between', gap: '12px', alignItems: 'baseline' }}>
              <strong>Fakturerbar værdi pr. medarbejder</strong>
              <span style={{ color: 'var(--text-secondary)', fontSize: '12px' }}>Top {employeeValues.length}</span>
            </div>
            {employeeValues.length ? employeeValues.map((employee) => (
              <div className="overview-analytics-row" key={employee.userId}>
                <span>
                  <strong style={{ display: 'block' }}>{employee.employee}</strong>
                  <small style={{ color: 'var(--text-secondary)' }}>{employee.hours.toFixed(1)} t · {formatCurrency(employee.effectiveRate)}/t effektiv sats</small>
                </span>
                <strong>{formatCurrency(employee.amount)}</strong>
              </div>
            )) : <p className="overview-analytics-empty">Ingen registrerede timer i perioden.</p>}
          </div>
        </div>
      ) : (
        <div id="overview-analytics-panel-customers" className="overview-customer-growth" role="tabpanel" aria-label="Nye kunder pr. måned">
          {customerGrowth.map((item) => <div className="overview-customer-growth__item" key={item.key}><strong>{item.count}</strong><div className="overview-customer-growth__bar" aria-hidden="true"><span style={{ height: `${Math.max(8, (item.count / maxCustomerCount) * 100)}%` }} /></div><span>{item.label}</span></div>)}
        </div>
      )}
    </section>
  );
}
