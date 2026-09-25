import { useQuery } from '@tanstack/react-query';
import {
  Activity,
  AlertTriangle,
  ArrowLeft,
  BadgeCheck,
  BarChart3,
  Clock3,
  RefreshCw,
  RotateCcw,
  Send,
  Timer,
} from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { getOrganizations, getSuperadminErrorMessage, superadminOrganizationQueryKey } from '../api';
import {
  getWorkflowStatistics,
  workflowStatisticsQueryKey,
  type DistributionBucket,
  type DurationMetric,
  type WorkflowTrendPoint,
} from '../statisticsApi';
import './SuperAdminStatistics.css';

const dayOptions = [30, 90, 180, 365] as const;

const formatNumber = (value: number, maximumFractionDigits = 1) =>
  new Intl.NumberFormat('da-DK', { maximumFractionDigits }).format(value);

const formatPercent = (value: number | null) =>
  value === null ? '—' : `${formatNumber(value * 100, 1)} %`;

const formatDuration = (seconds: number | null) => {
  if (seconds === null) return '—';
  if (seconds < 60) return `${Math.round(seconds)} sek`;
  if (seconds < 3_600) return `${formatNumber(seconds / 60)} min`;
  if (seconds < 172_800) return `${formatNumber(seconds / 3_600)} t`;
  return `${formatNumber(seconds / 86_400)} dage`;
};

function MetricCard({
  label,
  description,
  metric,
  icon,
  primary = false,
}: {
  label: string;
  description: string;
  metric: DurationMetric;
  icon: ReactNode;
  primary?: boolean;
}) {
  return (
    <article className={`statistics-metric-card${primary ? ' statistics-metric-card--primary' : ''}`}>
      <div className="statistics-metric-icon" aria-hidden="true">{icon}</div>
      <div className="statistics-metric-copy">
        <span>{label}</span>
        <strong>{formatDuration(metric.medianSeconds)}</strong>
        <small>{description}</small>
        <dl>
          <div><dt>Gns.</dt><dd>{formatDuration(metric.averageSeconds)}</dd></div>
          <div><dt>P75</dt><dd>{formatDuration(metric.p75Seconds)}</dd></div>
          <div><dt>Datapunkter</dt><dd>{metric.sampleSize}</dd></div>
        </dl>
      </div>
    </article>
  );
}

function Sparkline({
  points,
  value,
  formatter = formatDuration,
  emptyText = 'Ikke nok data endnu',
}: {
  points: WorkflowTrendPoint[];
  value: (point: WorkflowTrendPoint) => number | null;
  formatter?: (value: number | null) => string;
  emptyText?: string;
}) {
  const values = points.map(value).filter((item): item is number => item !== null);
  if (values.length < 2) {
    return <div className="statistics-chart-empty">{emptyText}</div>;
  }

  const width = 420;
  const height = 120;
  const inset = 10;
  const min = Math.min(...values);
  const max = Math.max(...values);
  const range = Math.max(max - min, 1);
  const available = points
    .map((point, index) => ({ index, value: value(point) }))
    .filter((item): item is { index: number; value: number } => item.value !== null);
  const polyline = available.map(({ index, value: itemValue }) => {
    const x = inset + (index / Math.max(points.length - 1, 1)) * (width - inset * 2);
    const y = height - inset - ((itemValue - min) / range) * (height - inset * 2);
    return `${x},${y}`;
  }).join(' ');

  const latest = available.at(-1)?.value ?? null;

  return (
    <div className="statistics-sparkline">
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label={`Seneste værdi ${formatter(latest)}`}>
        <line x1="10" y1="110" x2="410" y2="110" className="statistics-chart-axis" />
        <polyline points={polyline} className="statistics-chart-line" />
        {available.map(({ index, value: itemValue }) => {
          const x = inset + (index / Math.max(points.length - 1, 1)) * (width - inset * 2);
          const y = height - inset - ((itemValue - min) / range) * (height - inset * 2);
          return <circle key={`${index}-${itemValue}`} cx={x} cy={y} r="3.5" className="statistics-chart-dot" />;
        })}
      </svg>
      <div className="statistics-chart-range">
        <span>{formatter(min)}</span>
        <strong>{formatter(latest)}</strong>
        <span>{formatter(max)}</span>
      </div>
    </div>
  );
}

function Distribution({ buckets }: { buckets: DistributionBucket[] }) {
  const maxRate = Math.max(...buckets.map((bucket) => bucket.rate), 0.01);
  return (
    <div className="statistics-bars">
      {buckets.map((bucket) => (
        <div className="statistics-bar-row" key={bucket.key}>
          <span>{bucket.label}</span>
          <div className="statistics-bar-track" aria-hidden="true">
            <div className="statistics-bar-fill" style={{ width: `${Math.max(2, (bucket.rate / maxRate) * 100)}%` }} />
          </div>
          <strong>{formatPercent(bucket.rate)}</strong>
          <small>{bucket.count}</small>
        </div>
      ))}
    </div>
  );
}

export function SuperAdminStatistics() {
  const navigate = useNavigate();
  const [days, setDays] = useState<number>(90);
  const [organizationId, setOrganizationId] = useState('');

  const organizationsQuery = useQuery({
    queryKey: superadminOrganizationQueryKey,
    queryFn: getOrganizations,
  });
  const organizations = useMemo(
    () => [...(organizationsQuery.data ?? [])].sort((left, right) => left.name.localeCompare(right.name, 'da')),
    [organizationsQuery.data],
  );

  const statisticsQuery = useQuery({
    queryKey: workflowStatisticsQueryKey(days, organizationId || undefined),
    queryFn: () => getWorkflowStatistics({ days, organizationId: organizationId || undefined }),
    refetchInterval: 60_000,
  });

  const data = statisticsQuery.data;
  const summary = data?.summary;
  const trend = data?.trend ?? [];
  const isEmpty = !statisticsQuery.isPending && (summary?.caseCount ?? 0) === 0;

  const waiting = summary?.assignedToFirstWork.medianSeconds ?? 0;
  const active = summary?.employeeActive.medianSeconds ?? 0;
  const inactive = summary?.inactiveAfterStart.medianSeconds ?? 0;
  const breakdownTotal = Math.max(waiting + active + inactive, 1);

  return (
    <div className="page-container statistics-page">
      <header className="statistics-header">
        <div>
          <button type="button" className="statistics-back" onClick={() => navigate('/superadmin')}>
            <ArrowLeft size={17} aria-hidden="true" /> Superadmin
          </button>
          <div className="statistics-title-row">
            <span className="statistics-title-icon" aria-hidden="true"><BarChart3 size={26} /></span>
            <div>
              <h1>Statistik</h1>
              <p>Se hvor hurtigt Workslip-flowet fungerer, og hvor kvaliteten kan forbedres.</p>
            </div>
          </div>
        </div>

        <div className="statistics-filters" aria-label="Filtre for statistik">
          <label>
            <span>Virksomhed</span>
            <select value={organizationId} onChange={(event) => setOrganizationId(event.target.value)}>
              <option value="">Alle virksomheder</option>
              {organizations.map((organization) => (
                <option key={organization.id} value={organization.id}>{organization.name}</option>
              ))}
            </select>
          </label>
          <label>
            <span>Periode</span>
            <select value={days} onChange={(event) => setDays(Number(event.target.value))}>
              {dayOptions.map((option) => <option key={option} value={option}>Seneste {option} dage</option>)}
            </select>
          </label>
          <button
            type="button"
            className="btn btn-secondary statistics-refresh"
            onClick={() => { void statisticsQuery.refetch(); }}
            disabled={statisticsQuery.isFetching}
          >
            <RefreshCw size={16} className={statisticsQuery.isFetching ? 'animate-spin' : undefined} aria-hidden="true" />
            Opdatér
          </button>
        </div>
      </header>

      {(statisticsQuery.isError || organizationsQuery.isError) && (
        <div className="superadmin-alert superadmin-alert-error" role="alert">
          <span>{getSuperadminErrorMessage(statisticsQuery.error ?? organizationsQuery.error)}</span>
          <button type="button" className="btn btn-secondary" onClick={() => { void statisticsQuery.refetch(); }}>Prøv igen</button>
        </div>
      )}

      <section className="statistics-section" aria-labelledby="workflow-performance-title">
        <div className="statistics-section-heading">
          <div>
            <span className="statistics-eyebrow"><Activity size={15} /> Workflow performance</span>
            <h2 id="workflow-performance-title">De tre målinger vi styrer efter</h2>
            <p>Medianen er hovedtallet. Gennemsnit og P75 vises ved siden af, så enkelte lange sager ikke skjuler det normale flow.</p>
          </div>
          <span className="statistics-live-badge">Live Workslip-data</span>
        </div>

        <div className="statistics-primary-grid" aria-live="polite">
          <MetricCard
            label="Oprettet → godkendt"
            description="Samlet kalendertid fra sagen oprettes til endelig godkendelse."
            metric={summary?.createdToApproved ?? { averageSeconds: null, medianSeconds: null, p75Seconds: null, sampleSize: 0 }}
            icon={<BadgeCheck size={19} />}
            primary
          />
          <MetricCard
            label="Medarbejderens aktive udfyldelsestid"
            description="Kun registreret aktiv tid. Pauser og skjulte faner tæller ikke med."
            metric={summary?.employeeActive ?? { averageSeconds: null, medianSeconds: null, p75Seconds: null, sampleSize: 0 }}
            icon={<Timer size={19} />}
          />
          <MetricCard
            label="Sendt → medarbejder indsender"
            description="Kalendertid fra tildeling til medarbejderen sender sagen til admin."
            metric={summary?.assignedToSubmitted ?? { averageSeconds: null, medianSeconds: null, p75Seconds: null, sampleSize: 0 }}
            icon={<Send size={19} />}
          />
        </div>

        <div className="statistics-breakdown-card">
          <div>
            <span className="statistics-card-label">Hvor ligger tiden før indsendelse?</span>
            <h3>Ventetid kontra aktiv udfyldelse</h3>
            <p>Gør det synligt, om en lang sag skyldes Workslip-flowet eller at arbejdet først bliver udført senere.</p>
          </div>
          <div className="statistics-stacked" role="img" aria-label="Fordeling mellem ventetid, aktiv tid og øvrig inaktiv tid">
            <span className="statistics-stack-wait" style={{ width: `${(waiting / breakdownTotal) * 100}%` }} />
            <span className="statistics-stack-active" style={{ width: `${(active / breakdownTotal) * 100}%` }} />
            <span className="statistics-stack-inactive" style={{ width: `${(inactive / breakdownTotal) * 100}%` }} />
          </div>
          <div className="statistics-stack-legend">
            <span><i className="statistics-legend-dot statistics-legend-dot--wait" />Før første arbejde <strong>{formatDuration(waiting)}</strong></span>
            <span><i className="statistics-legend-dot statistics-legend-dot--active" />Aktiv udfyldelse <strong>{formatDuration(active)}</strong></span>
            <span><i className="statistics-legend-dot statistics-legend-dot--inactive" />Øvrig inaktiv tid <strong>{formatDuration(inactive)}</strong></span>
          </div>
        </div>

        <div className="statistics-trend-grid">
          <article className="statistics-chart-card">
            <span>Oprettet → godkendt</span><h3>Udvikling pr. uge</h3>
            <Sparkline points={trend} value={(point) => point.createdToApprovedMedianSeconds} />
          </article>
          <article className="statistics-chart-card">
            <span>Aktiv udfyldelsestid</span><h3>Udvikling pr. uge</h3>
            <Sparkline points={trend} value={(point) => point.employeeActiveMedianSeconds} emptyText="Aktiv telemetry opsamles fra nu af" />
          </article>
          <article className="statistics-chart-card">
            <span>Sendt → indsendt</span><h3>Udvikling pr. uge</h3>
            <Sparkline points={trend} value={(point) => point.assignedToSubmittedMedianSeconds} />
          </article>
        </div>

        <div className="statistics-distribution-grid">
          <article className="statistics-chart-card">
            <span>Medarbejderflow</span><h3>Fordeling af aktiv udfyldelsestid</h3>
            <Distribution buckets={data?.employeeActiveDistribution ?? []} />
          </article>
          <article className="statistics-chart-card">
            <span>Gennemløbstid</span><h3>Fordeling fra sendt til indsendt</h3>
            <Distribution buckets={data?.assignedToSubmittedDistribution ?? []} />
          </article>
        </div>
      </section>

      <section className="statistics-section" aria-labelledby="quality-title">
        <div className="statistics-section-heading">
          <div>
            <span className="statistics-eyebrow"><BadgeCheck size={15} /> Quality & rejections</span>
            <h2 id="quality-title">Hvor ofte lykkes flowet første gang?</h2>
            <p>Positive kvalitetsmål står side om side med fejl og rework, så dashboardet viser både stabilitet og forbedringsmuligheder.</p>
          </div>
        </div>

        <div className="statistics-quality-grid">
          <article><BadgeCheck size={18} /><span>Godkendt første gang</span><strong>{formatPercent(summary?.firstPassApprovalRate ?? null)}</strong><small>Af godkendte sager</small></article>
          <article><AlertTriangle size={18} /><span>Sager med afvisning</span><strong>{formatPercent(summary?.rejectionRate ?? null)}</strong><small>Af indsendte sager</small></article>
          <article><Activity size={18} /><span>System/UI-signal</span><strong>{formatPercent(summary?.systemUiRejectionRate ?? null)}</strong><small>Af klassificerede afvisninger</small></article>
          <article><RotateCcw size={18} /><span>Rework pr. godkendt sag</span><strong>{summary?.averageReworkLoops == null ? '—' : formatNumber(summary.averageReworkLoops, 2)}</strong><small>{summary?.rejectionEventCount ?? 0} afvisningshændelser</small></article>
        </div>

        <div className="statistics-quality-layout">
          <article className="statistics-chart-card">
            <span>First-pass vs. rework</span>
            <h3>Godkendt uden retur</h3>
            <div className="statistics-approval-stack" role="img" aria-label="Andel godkendt første gang og andel med rework">
              <span className="statistics-approval-pass" style={{ width: `${(summary?.firstPassApprovalRate ?? 0) * 100}%` }} />
              <span className="statistics-approval-rework" style={{ width: `${Math.max(0, 1 - (summary?.firstPassApprovalRate ?? 0)) * 100}%` }} />
            </div>
            <div className="statistics-stack-legend">
              <span><i className="statistics-legend-dot statistics-legend-dot--active" />Første gang <strong>{formatPercent(summary?.firstPassApprovalRate ?? null)}</strong></span>
              <span><i className="statistics-legend-dot statistics-legend-dot--rework" />Har krævet rework <strong>{summary?.firstPassApprovalRate == null ? '—' : formatPercent(1 - summary.firstPassApprovalRate)}</strong></span>
            </div>
            <div className="statistics-quality-trend">
              <h4>First-pass pr. uge</h4>
              <Sparkline
                points={trend}
                value={(point) => point.firstPassApprovalRate == null ? null : point.firstPassApprovalRate * 100}
                formatter={(value) => value == null ? '—' : `${formatNumber(value, 0)} %`}
              />
            </div>
          </article>

          <article className="statistics-chart-card">
            <span>Afvisningsårsager</span>
            <h3>Hvorfor bliver sager sendt tilbage?</h3>
            <p className="statistics-card-note">
              Årsager klassificeres fra eksisterende afvisningsbegrundelser. “System/UI” er et produktsignal — ikke automatisk bevis for årsag.
            </p>
            <Distribution buckets={(data?.rejectionReasons ?? []).map((reason) => ({ key: reason.code, label: reason.label, count: reason.count, rate: reason.rate }))} />
            <small className="statistics-coverage-note">
              Klassificeringsdækning: {formatPercent(summary?.rejectionReasonCoverageRate ?? null)}. Uklassificerede historiske afvisninger beholdes synligt.
            </small>
          </article>
        </div>
      </section>

      <section className="statistics-section" aria-labelledby="organization-statistics-title">
        <div className="statistics-section-heading">
          <div>
            <span className="statistics-eyebrow"><Clock3 size={15} /> Virksomheder</span>
            <h2 id="organization-statistics-title">Sammenlign uden at blande tenant-data</h2>
            <p>SuperAdmin kan se tværgående statistik; normale admins får aldrig adgang til denne tværgående visning.</p>
          </div>
        </div>
        <div className="statistics-table-scroll">
          <table className="statistics-table">
            <thead><tr><th>Virksomhed</th><th>Sager</th><th>Oprettet → godkendt</th><th>Aktiv udfyldelse</th><th>Sendt → indsendt</th><th>First-pass</th><th>Afvist</th></tr></thead>
            <tbody>
              {(data?.organizations ?? []).map((organization) => (
                <tr key={organization.organizationId}>
                  <td><strong>{organization.organizationName}</strong><small>{organization.submittedCount} indsendt · {organization.approvedCount} godkendt</small></td>
                  <td>{organization.caseCount}</td>
                  <td>{formatDuration(organization.createdToApproved.medianSeconds)}</td>
                  <td>{formatDuration(organization.employeeActive.medianSeconds)}</td>
                  <td>{formatDuration(organization.assignedToSubmitted.medianSeconds)}</td>
                  <td>{formatPercent(organization.firstPassApprovalRate)}</td>
                  <td>{formatPercent(organization.rejectionRate)}</td>
                </tr>
              ))}
              {isEmpty && <tr><td colSpan={7} className="statistics-empty-cell">Ingen sager i den valgte periode.</td></tr>}
            </tbody>
          </table>
        </div>
      </section>

      {isEmpty && (
        <div className="statistics-empty" role="status">
          <BarChart3 size={24} aria-hidden="true" />
          <div><strong>Ingen statistik endnu</strong><span>Vælg en længere periode eller en anden virksomhed.</span></div>
        </div>
      )}
    </div>
  );
}