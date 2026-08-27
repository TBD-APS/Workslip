import { useQuery } from '@tanstack/react-query';
import { AlertTriangle, ArrowRight, Banknote, BarChart3, CheckCircle2, ClipboardList, Clock3, ExternalLink, TrendingUp, Users, WalletCards, XCircle } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { ErrorState } from '../../../components/ErrorState';
import { formatDateTimeShort } from '../../../lib/formatDate';
import { fetchLeaderAnalysisSummary, fetchLeaderEconomicsSummary, leaderAnalysisQueryKey, leaderEconomicsQueryKey } from '../api';
import { AdminPowerBiJobStatusChart } from '../../overview/components/AdminPowerBiJobStatusChart';
import { apiClient } from '../../../lib/axios';
import { LeaderVisualsPanel } from '../components/LeaderVisualsPanel';
import '../../overview/routes/Overview.css';
import './Lederanalyse.css';

const formatPercent = (value: number | null) => {
  if (value === null) return '—';
  return `${Math.round(value * 100)} %`;
};

const formatCurrency = (value: number) =>
  new Intl.NumberFormat('da-DK', { style: 'currency', currency: 'DKK', maximumFractionDigits: 0 }).format(value);

export function Lederanalyse() {
  const navigate = useNavigate();
  const query = useQuery({
    queryKey: leaderAnalysisQueryKey,
    queryFn: fetchLeaderAnalysisSummary,
    refetchInterval: 30_000,
  });
  const economicsQuery = useQuery({
    queryKey: leaderEconomicsQueryKey,
    queryFn: fetchLeaderEconomicsSummary,
    refetchInterval: 30_000,
  });

  if (query.isError) {
    return (
      <div id="leader-analysis-page" className="page-container leader-analysis-page">
        <ErrorState message="Kunne ikke hente lederanalysen." onRetry={() => void query.refetch()} />
      </div>
    );
  }

  const summary = query.data;
  const isLoading = query.isPending;

  const showRiskBanner = (summary?.inReviewCount ?? 0) >= 5 || (summary?.rejectedCount ?? 0) >= 3;

  return (
    <div id="leader-analysis-page" className="page-container leader-analysis-page">
      <div className="page-header leader-analysis-header">
        <div>
          <h2 id="leader-analysis-heading">Lederanalyse</h2>
          <p>Driftsnøgletal for bemanding, kvalitet og sagsøkonomi – samme datagrundlag som Overblik, samlet til ledelsesbeslutninger.</p>
        </div>
        <button
          id="leader-analysis-go-overview"
          type="button"
          className="btn btn-secondary"
          onClick={() => navigate('/app/overblik')}
        >
          Gå til Overblik <ArrowRight size={16} aria-hidden="true" />
        </button>
      </div>

      {showRiskBanner && summary && (
        <div id="leader-analysis-risk-banner" className="leader-risk-banner" role="status" aria-live="polite">
          <AlertTriangle size={18} aria-hidden="true" />
          <span>
            Flaskehals: <strong>{summary.inReviewCount}</strong> sager afventer godkendelse
            {summary.rejectedCount > 0 ? <> · <strong>{summary.rejectedCount}</strong> afvist(e) – kræver opfølgning</> : null}.
            Prioritér gennemsyn for at frigive fakturering.
          </span>
        </div>
      )}

      <section id="leader-analysis-powerbi" aria-labelledby="leader-powerbi-heading" style={{ marginBottom: '0.5rem' }}>
        <div className="leader-analysis-card" style={{ padding: 0, overflow: 'hidden' }}>
          <div style={{ padding: '1rem 1rem 0' }}>
            <h3 id="leader-powerbi-heading" style={{ margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
              <BarChart3 size={18} aria-hidden="true" /> Power BI — virksomhedsstatistik
            </h3>
            <p style={{ margin: '0.25rem 0 0', color: 'var(--text-secondary)', fontSize: '0.88rem' }}>
              Visualisering af sagsfordeling, medarbejderøkonomi og kundetilvækst — samme datagrundlag som tidligere Overblik, nu samlet i Lederanalyse for maksimalt overblik.
            </p>
          </div>
          <AdminPowerBiJobStatusChart />
        </div>
      </section>

      <section id="leader-analysis-economics" className="leader-analysis-card" aria-labelledby="leader-economics-heading">
        <div className="leader-analysis-card__header">
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <h3 id="leader-economics-heading" style={{ display: 'flex', alignItems: 'center', gap: '8px', margin: 0 }}>
              <WalletCards size={18} aria-hidden="true" /> Økonomi & bilag
            </h3>
            {economicsQuery.data && (
              <span
                id="leader-economics-provider-badge"
                style={{
                  fontSize: '11px',
                  fontWeight: 700,
                  letterSpacing: '0.05em',
                  textTransform: 'uppercase',
                  padding: '4px 8px',
                  borderRadius: '999px',
                  background: 'var(--bg)',
                  border: '1px solid var(--border)',
                  color: 'var(--muted)',
                }}
              >
                {economicsQuery.data.providerDisplayName}
              </span>
            )}
          </div>
          <p>Eksterne fakturaer og bilag fra {economicsQuery.data?.providerDisplayName ?? 'regnskabssystemet'} — beløb er altid i fokus.</p>
        </div>

        {economicsQuery.isPending ? (
          <div className="leader-analysis-empty">Henter økonomidata…</div>
        ) : economicsQuery.isError ? (
          <div className="leader-analysis-empty" role="alert">
            Kunne ikke hente bilag fra {economicsQuery.data?.providerDisplayName ?? 'regnskabssystemet'}. Vælg regnskabsintegration under Superadmin → Organisation.
          </div>
        ) : (
          <>
            <div className="leader-analysis-kpi-grid" style={{ padding: '1rem', gap: '0.9rem' }}>
              <div id="leader-economics-total" className="leader-kpi-card" style={{ borderLeft: '3px solid var(--status-green-text)' }}>
                <span className="leader-kpi-card__label"><Banknote size={14} aria-hidden="true" /> Samlet beløb</span>
                <strong className="leader-kpi-card__value">{formatCurrency(economicsQuery.data.totalAmount)}</strong>
                <span className="leader-kpi-card__hint">{economicsQuery.data.documentCount} bilag i alt</span>
              </div>
              <div id="leader-economics-invoices" className="leader-kpi-card">
                <span className="leader-kpi-card__label">Fakturaer</span>
                <strong className="leader-kpi-card__value">{economicsQuery.data.invoiceCount}</strong>
                <span className="leader-kpi-card__hint">Heraf {formatCurrency(economicsQuery.data.totalAmount)} i alt</span>
              </div>
              <div id="leader-economics-receipts" className="leader-kpi-card">
                <span className="leader-kpi-card__label">Bilag / kvitteringer</span>
                <strong className="leader-kpi-card__value">{economicsQuery.data.receiptCount}</strong>
                <span className="leader-kpi-card__hint">{economicsQuery.data.receiptCount} stk. i perioden</span>
              </div>
              <div id="leader-economics-average" className="leader-kpi-card">
                <span className="leader-kpi-card__label">Gennemsnit pr. bilag</span>
                <strong className="leader-kpi-card__value">{formatCurrency(economicsQuery.data.averageAmount)}</strong>
                <span className="leader-kpi-card__hint">Seneste 6 måneder</span>
              </div>
            </div>

            <div style={{ borderTop: '1px solid var(--border)', padding: '0' }}>
              <div style={{ padding: '0.9rem 1rem 0.5rem', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                <strong style={{ fontSize: '13px' }}>Seneste bilag fra {economicsQuery.data.providerDisplayName}</strong>
                <span style={{ fontSize: '12px', color: 'var(--muted)' }}>{economicsQuery.data.recentDocuments.length} vist</span>
              </div>
              {economicsQuery.data.recentDocuments.length === 0 ? (
                <div className="leader-analysis-empty">Ingen bilag i perioden — skift til Mock eller e-conomic (demo) i Superadmin.</div>
              ) : (
                <div className="leader-analysis-recent-list" style={{ borderTop: '1px solid var(--border)' }}>
                  {economicsQuery.data.recentDocuments.map((doc) => (
                    <div key={doc.documentId} className="leader-analysis-recent-row" style={{ gridTemplateColumns: 'minmax(0,1fr) auto auto auto' }}>
                      <div>
                        <strong>{doc.documentNumber}</strong>
                        <div><small>{doc.type === 'Invoice' ? 'Faktura' : 'Bilag'} · {doc.date} · {doc.status}</small></div>
                      </div>
                      <strong style={{ fontVariantNumeric: 'tabular-nums' }}>{formatCurrency(doc.amount)}</strong>
                      <small style={{ color: 'var(--muted)' }}>{doc.type}</small>
                      <a
                        id={`leader-economics-open-${doc.documentId}`}
                        href={doc.externalLink}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="btn btn-secondary"
                        style={{ padding: '6px 10px', fontSize: '12px', display: 'inline-flex', alignItems: 'center', gap: '6px' }}
                      >
                        Åbn <ExternalLink size={12} aria-hidden="true" />
                      </a>
                    </div>
                  ))}
                </div>
              )}
              <div style={{ padding: '0.6rem 1rem 1rem', fontSize: '12px', color: 'var(--muted)' }}>
                Viser bilag via valgt regnskabsudbyder. Skift udbyder: <button type="button" onClick={() => navigate('/superadmin')} style={{ background: 'none', border: 0, color: 'var(--brand)', cursor: 'pointer', textDecoration: 'underline', padding: 0, font: 'inherit' }}>Superadmin → Regnskabsintegration</button>. Demo e-conomic bruger <code>X-AgreementGrantToken: demo</code>.
              </div>
            </div>
          </>
        )}
      </section>

      <section className="leader-analysis-kpi-grid" aria-label="Nøgletal">
        <div id="leader-analysis-kpi-active" className="leader-kpi-card leader-kpi-card--active">
          <span className="leader-kpi-card__label"><ClipboardList size={14} aria-hidden="true" /> Aktive sager</span>
          <strong className="leader-kpi-card__value">{isLoading ? '—' : summary?.activeCount ?? 0}</strong>
          <span className="leader-kpi-card__hint">Igangværende sager i drift</span>
        </div>
        <div id="leader-analysis-kpi-review" className="leader-kpi-card leader-kpi-card--review">
          <span className="leader-kpi-card__label"><Clock3 size={14} aria-hidden="true" /> Til gennemsyn</span>
          <strong className="leader-kpi-card__value">{isLoading ? '—' : summary?.inReviewCount ?? 0}</strong>
          <span className="leader-kpi-card__hint">Afventer ledergodkendelse</span>
        </div>
        <div id="leader-analysis-kpi-approved" className="leader-kpi-card leader-kpi-card--approved">
          <span className="leader-kpi-card__label"><CheckCircle2 size={14} aria-hidden="true" /> Godkendte</span>
          <strong className="leader-kpi-card__value">{isLoading ? '—' : summary?.approvedCount ?? 0}</strong>
          <span className="leader-kpi-card__hint">Klar til fakturering / arkiv</span>
        </div>
        <div id="leader-analysis-kpi-rejected" className="leader-kpi-card leader-kpi-card--rejected">
          <span className="leader-kpi-card__label"><XCircle size={14} aria-hidden="true" /> Afviste</span>
          <strong className="leader-kpi-card__value">{isLoading ? '—' : summary?.rejectedCount ?? 0}</strong>
          <span className="leader-kpi-card__hint">Kræver genbesøg eller rettelse</span>
        </div>
      </section>

      <section className="leader-analysis-kpi-grid" aria-label="Afledte nøgletal">
        <div id="leader-analysis-kpi-total" className="leader-kpi-card leader-kpi-card--total">
          <span className="leader-kpi-card__label"><BarChart3 size={14} aria-hidden="true" /> Total portefølje</span>
          <strong className="leader-kpi-card__value">{isLoading ? '—' : summary?.totalCount ?? 0}</strong>
          <span className="leader-kpi-card__hint">Alle sager i systemet</span>
        </div>
        <div id="leader-analysis-kpi-approval-rate" className="leader-kpi-card">
          <span className="leader-kpi-card__label"><TrendingUp size={14} aria-hidden="true" /> Godkendelsesgrad</span>
          <strong className="leader-kpi-card__value">{isLoading ? '—' : formatPercent(summary?.approvalRate ?? null)}</strong>
          <span className="leader-kpi-card__hint">Andel godkendte af afgjorte sager</span>
        </div>
        <div id="leader-analysis-kpi-rejection-rate" className="leader-kpi-card">
          <span className="leader-kpi-card__label"><AlertTriangle size={14} aria-hidden="true" /> Afvisningsgrad</span>
          <strong className="leader-kpi-card__value">{isLoading ? '—' : formatPercent(summary?.rejectionRate ?? null)}</strong>
          <span className="leader-kpi-card__hint">Andel afviste af afgjorte sager</span>
        </div>
        <div id="leader-analysis-kpi-flow" className="leader-kpi-card">
          <span className="leader-kpi-card__label"><Users size={14} aria-hidden="true" /> Bemanding (kommende)</span>
          <strong className="leader-kpi-card__value">—</strong>
          <span className="leader-kpi-card__hint">Kobles til Folk/Timer i næste iteration</span>
        </div>
      </section>

      <div className="leader-analysis-secondary-grid">
        <section id="leader-analysis-flow-panel" className="leader-analysis-card" aria-labelledby="leader-flow-heading">
          <div className="leader-analysis-card__header">
            <h3 id="leader-flow-heading">Sagsflow</h3>
            <p>Fordeling på tværs af status – samme datagrundlag som Overblik.</p>
          </div>
          <div className="leader-analysis-card__body">
            <div className="leader-metric-row">
              <span className="leader-metric-row__label">Aktive</span>
              <strong className="leader-metric-row__value">{isLoading ? '—' : summary?.activeCount ?? 0}</strong>
            </div>
            <div className="leader-metric-row">
              <span className="leader-metric-row__label">Til gennemsyn</span>
              <strong className="leader-metric-row__value">{isLoading ? '—' : summary?.inReviewCount ?? 0}</strong>
            </div>
            <div className="leader-metric-row">
              <span className="leader-metric-row__label">Godkendte</span>
              <strong className="leader-metric-row__value">{isLoading ? '—' : summary?.approvedCount ?? 0}</strong>
            </div>
            <div className="leader-metric-row">
              <span className="leader-metric-row__label">Afviste</span>
              <strong className="leader-metric-row__value">{isLoading ? '—' : summary?.rejectedCount ?? 0}</strong>
            </div>
            <div className="leader-metric-row">
              <span className="leader-metric-row__label">Total</span>
              <strong className="leader-metric-row__value">{isLoading ? '—' : summary?.totalCount ?? 0}</strong>
            </div>
          </div>
        </section>

        <section id="leader-analysis-quality-panel" className="leader-analysis-card" aria-labelledby="leader-quality-heading">
          <div className="leader-analysis-card__header">
            <h3 id="leader-quality-heading">Kvalitet & opfølgning</h3>
            <p>Indikatorer for hvor ledelsesindsats giver størst effekt.</p>
          </div>
          <div className="leader-analysis-card__body">
            <div className="leader-metric-row">
              <span className="leader-metric-row__label">Godkendelsesgrad</span>
              <strong className="leader-metric-row__value">{isLoading ? '—' : formatPercent(summary?.approvalRate ?? null)}</strong>
            </div>
            <div className="leader-metric-row">
              <span className="leader-metric-row__label">Afvisningsgrad</span>
              <strong className="leader-metric-row__value">{isLoading ? '—' : formatPercent(summary?.rejectionRate ?? null)}</strong>
            </div>
            <div className="leader-metric-row">
              <span className="leader-metric-row__label">Sager til gennemsyn</span>
              <strong className="leader-metric-row__value">{isLoading ? '—' : summary?.inReviewCount ?? 0} – {isLoading ? '—' : (summary && summary.inReviewCount > 0 ? 'prioriter gennemsyn' : 'ingen flaskehals')}</strong>
            </div>
            <div className="leader-metric-row">
              <span className="leader-metric-row__label">Dokumentation (kommende)</span>
              <strong className="leader-metric-row__value">—</strong>
            </div>
          </div>
        </section>
      </div>

      <section id="leader-analysis-recent" className="leader-analysis-card" aria-labelledby="leader-recent-heading">
        <div className="leader-analysis-card__header">
          <h3 id="leader-recent-heading">Seneste sager</h3>
          <p>De senest opdaterede sager – genbrug fra Overblik, filtrerbar i næste iteration.</p>
        </div>
        {isLoading ? (
          <div className="leader-analysis-empty">Henter sager…</div>
        ) : (summary?.recentJobs.length ?? 0) === 0 ? (
          <div className="leader-analysis-empty">Ingen sager at vise.</div>
        ) : (
          <div className="leader-analysis-recent-list">
            {summary!.recentJobs.map((job) => (
              <div key={job.id} className="leader-analysis-recent-row">
                <div>
                  <strong>{job.reportNumber ? `SAG-${job.reportNumber}` : job.id.slice(0, 8)}</strong>
                  <div><small>{job.customerName ?? 'Uden kunde'} · {job.status}</small></div>
                </div>
                <small>{formatDateTimeShort(job.updatedAt)}</small>
                <button
                  id={`leader-analysis-open-${job.id}`}
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => navigate(job.status === 'InReview' || job.status === 'Approved' ? `/app/completed/${job.id}` : `/app/job/${job.id}`)}
                >
                  Åbn
                </button>
              </div>
            ))}
          </div>
        )}
      </section>

      <LeaderBemandingPanel />
      <LeaderSagsokonomiPanel summary={summary} isLoading={isLoading} />
      <LeaderSlaPanel />
      <LeaderVisualsPanel />
      <LeaderExportPanel summary={summary} economics={economicsQuery.data} />
    </div>
  );
}

function LeaderBemandingPanel() {
  const query = useQuery({
    queryKey: ['leader-analysis', 'bemanding'],
    queryFn: async () => {
      const data = (await apiClient.get('/api/worksheets/all/report/power-bi/data?historyMonths=24', { skipGlobalErrorToast: true })) as unknown as {
        employees: Array<{ userId: string; employee: string }>;
        workHours: Array<{ userId: string; hours: number }>;
      };
      return data;
    },
    staleTime: 30_000,
  });

  const employees = query.data?.employees ?? [];
  const workHours = query.data?.workHours ?? [];
  const hoursByUser = workHours.reduce<Record<string, number>>((acc, r) => {
    acc[r.userId] = (acc[r.userId] ?? 0) + (r.hours ?? 0);
    return acc;
  }, {});
  const totalHours = workHours.reduce((sum, r) => sum + (r.hours ?? 0), 0);
  const activeCount = employees.filter(e => (hoursByUser[e.userId] ?? 0) > 0).length;
  const avgHours = employees.length ? totalHours / employees.length : 0;
  const top = [...employees]
    .map(e => ({ ...e, hours: hoursByUser[e.userId] ?? 0 }))
    .sort((a, b) => b.hours - a.hours)
    .slice(0, 5);

  return (
    <section id="leader-analysis-bemanding" className="leader-analysis-card" aria-labelledby="bemanding-heading">
      <div className="leader-analysis-card__header">
        <h3 id="bemanding-heading" style={{ display: 'flex', alignItems: 'center', gap: '8px', margin: 0 }}><Users size={18} aria-hidden="true" /> Bemanding & belægning</h3>
        <p>Folk og timer — hvem trækker læsset, og hvor er der ledig kapacitet.</p>
      </div>
      {query.isPending ? (
        <div className="leader-analysis-empty">Henter bemanding…</div>
      ) : query.isError ? (
        <div className="leader-analysis-empty" role="alert">Kunne ikke hente bemanding.</div>
      ) : (
        <>
          <div className="leader-analysis-kpi-grid" style={{ padding: '1rem', gap: '0.9rem' }}>
            <div className="leader-kpi-card"><span className="leader-kpi-card__label">Medarbejdere i alt</span><strong className="leader-kpi-card__value">{employees.length}</strong><span className="leader-kpi-card__hint">I organisationen</span></div>
            <div className="leader-kpi-card"><span className="leader-kpi-card__label">Aktive med timer</span><strong className="leader-kpi-card__value">{activeCount}</strong><span className="leader-kpi-card__hint">Seneste 24 mdr.</span></div>
            <div className="leader-kpi-card"><span className="leader-kpi-card__label">Timer i alt</span><strong className="leader-kpi-card__value">{totalHours.toFixed(1)} t</strong><span className="leader-kpi-card__hint">Registrerede timer</span></div>
            <div className="leader-kpi-card"><span className="leader-kpi-card__label">Gns. pr. medarbejder</span><strong className="leader-kpi-card__value">{avgHours.toFixed(1)} t</strong><span className="leader-kpi-card__hint">Over 24 mdr.</span></div>
          </div>
          <div style={{ borderTop: '1px solid var(--border)', padding: '0.8rem 1rem' }}>
            <strong style={{ fontSize: '13px' }}>Top bemanding</strong>
            <div style={{ display: 'grid', gap: '6px', marginTop: '8px' }}>
              {top.length ? top.map(p => (
                <div key={p.userId} className="leader-metric-row" style={{ padding: '6px 0' }}><span className="leader-metric-row__label">{p.employee}</span><strong className="leader-metric-row__value">{p.hours.toFixed(1)} t</strong></div>
              )) : <span style={{ color: 'var(--muted)', fontSize: '13px' }}>Ingen timer registreret.</span>}
            </div>
          </div>
        </>
      )}
    </section>
  );
}

function LeaderSagsokonomiPanel({ summary, isLoading }: { summary: { totalCount: number } | undefined; isLoading: boolean }) {
  const ecoQuery = useQuery({
    queryKey: ['leader-analysis', 'sagsokonomi', 'powerbi'],
    queryFn: async () => {
      const data = (await apiClient.get('/api/worksheets/all/report/power-bi/data?historyMonths=24', { skipGlobalErrorToast: true })) as unknown as {
        workHours: Array<{ hours: number; billableAmount: number | null }>;
        jobs: Array<{ status: string }>;
      };
      return data;
    },
    staleTime: 30_000,
  });

  const workHours = ecoQuery.data?.workHours ?? [];
  const totalHours = workHours.reduce((s, r) => s + (r.hours ?? 0), 0);
  const totalBillable = workHours.reduce((s, r) => s + (r.billableAmount ?? 0), 0);
  const avgRate = totalHours ? totalBillable / totalHours : 0;
  const faktureringsgrad = summary?.totalCount ? (ecoQuery.data?.jobs?.filter(j => j.status === 'Approved').length ?? 0) / Math.max(1, summary.totalCount) : null;

  return (
    <section id="leader-analysis-sagsokonomi" className="leader-analysis-card" aria-labelledby="sagsokonomi-heading">
      <div className="leader-analysis-card__header">
        <h3 id="sagsokonomi-heading" style={{ display: 'flex', alignItems: 'center', gap: '8px', margin: 0 }}><Banknote size={18} aria-hidden="true" /> Sagsøkonomi pr. sag/kunde</h3>
        <p>Timer × sats og faktureringsgrad — omsætning pr. sag i porteføljen.</p>
      </div>
      {ecoQuery.isPending || isLoading ? (
        <div className="leader-analysis-empty">Henter sagsøkonomi…</div>
      ) : ecoQuery.isError ? (
        <div className="leader-analysis-empty" role="alert">Kunne ikke hente sagsøkonomi.</div>
      ) : (
        <div className="leader-analysis-kpi-grid" style={{ padding: '1rem', gap: '0.9rem' }}>
          <div className="leader-kpi-card"><span className="leader-kpi-card__label">Timer i alt</span><strong className="leader-kpi-card__value">{totalHours.toFixed(1)} t</strong><span className="leader-kpi-card__hint">24 mdr.</span></div>
          <div className="leader-kpi-card" style={{ borderLeft: '3px solid var(--status-green-text)' }}><span className="leader-kpi-card__label">Fakturerbar værdi</span><strong className="leader-kpi-card__value">{formatCurrency(totalBillable)}</strong><span className="leader-kpi-card__hint">Fra timeregistrering</span></div>
          <div className="leader-kpi-card"><span className="leader-kpi-card__label">Gns. sats</span><strong className="leader-kpi-card__value">{formatCurrency(avgRate)}/t</strong><span className="leader-kpi-card__hint">Billable / timer</span></div>
          <div className="leader-kpi-card"><span className="leader-kpi-card__label">Faktureringsgrad</span><strong className="leader-kpi-card__value">{faktureringsgrad !== null ? `${Math.round(faktureringsgrad * 100)} %` : '—'}</strong><span className="leader-kpi-card__hint">Godkendte / total sager</span></div>
        </div>
      )}
    </section>
  );
}

function LeaderSlaPanel() {
  const navigate = useNavigate();
  const query = useQuery({
    queryKey: ['leader-analysis', 'sla', 'inReview'],
    queryFn: async () => {
      const data = (await apiClient.get('/api/jobs', { params: { status: 'InReview', limit: 20 }, skipGlobalErrorToast: true })) as unknown as { items: Array<{ id: string; reportNumber: string | null; customer?: { name?: string | null } | null; updatedAt: string; destinationAddress?: string | null }> };
      const list = Array.isArray((data as unknown as { items: unknown[] })?.items) ? (data as unknown as { items: Array<{ id: string; reportNumber: string | null; customer?: { name?: string | null } | null; updatedAt: string }> }).items : [];
      return list;
    },
    staleTime: 30_000,
  });

  const now = Date.now();
  const rows = (query.data ?? []).map(job => {
    const days = Math.max(0, Math.floor((now - new Date(job.updatedAt).getTime()) / (1000 * 60 * 60 * 24)));
    let sla: 'ok' | 'warning' | 'overdue' = 'ok';
    if (days > 7) sla = 'overdue';
    else if (days > 2) sla = 'warning';
    return { ...job, days, sla };
  });

  return (
    <section id="leader-analysis-sla" className="leader-analysis-card" aria-labelledby="sla-heading">
      <div className="leader-analysis-card__header">
        <h3 id="sla-heading" style={{ display: 'flex', alignItems: 'center', gap: '8px', margin: 0 }}><Clock3 size={18} aria-hidden="true" /> Gennemløbstid & SLA — Til gennemsyn</h3>
        <p>Advarer når sager har ligget for længe til godkendelse (&gt;2 dage advarsel, &gt;7 dage overskredet).</p>
      </div>
      {query.isPending ? (
        <div className="leader-analysis-empty">Henter sager til gennemsyn…</div>
      ) : query.isError ? (
        <div className="leader-analysis-empty" role="alert">Kunne ikke hente SLA-data.</div>
      ) : rows.length === 0 ? (
        <div className="leader-analysis-empty">Ingen sager til gennemsyn — ingen SLA-risiko.</div>
      ) : (
        <div className="leader-analysis-recent-list" style={{ borderTop: '1px solid var(--border)' }}>
          {rows.map(job => (
            <div key={job.id} className="leader-analysis-recent-row" style={{ gridTemplateColumns: 'minmax(0,1fr) auto auto' }}>
              <div>
                <strong>{job.reportNumber ? `SAG-${job.reportNumber}` : job.id.slice(0, 8)}</strong>
                <div><small>{job.customer?.name ?? 'Uden kunde'} · {job.days} dage i gennemsyn</small></div>
              </div>
              <span style={{
                fontSize: '11px', fontWeight: 700, padding: '4px 8px', borderRadius: '999px',
                background: job.sla === 'overdue' ? '#fde8e8' : job.sla === 'warning' ? '#fef7d0' : '#e6f4ea',
                color: job.sla === 'overdue' ? '#b42318' : job.sla === 'warning' ? '#8a6a00' : '#1a7a3a',
                border: `1px solid ${job.sla === 'overdue' ? '#f5c2c2' : job.sla === 'warning' ? '#f0e0a0' : '#c6e7cf'}`,
              }}>
                {job.sla === 'overdue' ? 'Overskredet' : job.sla === 'warning' ? 'Advarsel' : 'OK'}
              </span>
              <button type="button" className="btn btn-secondary" style={{ padding: '6px 10px', fontSize: '12px' }} onClick={() => navigate(`/app/completed/${job.id}`)}>Åbn</button>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}

function LeaderExportPanel({ summary, economics }: { summary: { activeCount: number; inReviewCount: number; approvedCount: number; rejectedCount: number; totalCount: number; approvalRate: number | null; rejectionRate: number | null } | undefined; economics: { totalAmount: number; documentCount: number; providerDisplayName: string } | undefined }) {
  const handleCsv = () => {
    const rows = [
      ['Metric', 'Value'],
      ['Aktive', summary?.activeCount ?? ''],
      ['Til gennemsyn', summary?.inReviewCount ?? ''],
      ['Godkendte', summary?.approvedCount ?? ''],
      ['Afviste', summary?.rejectedCount ?? ''],
      ['Total', summary?.totalCount ?? ''],
      ['Godkendelsesgrad', summary?.approvalRate !== null && summary?.approvalRate !== undefined ? `${Math.round((summary.approvalRate as number) * 100)}%` : ''],
      ['Økonomi samlet', economics ? formatCurrency(economics.totalAmount) : ''],
      ['Bilag i alt', economics?.documentCount ?? ''],
      ['Provider', economics?.providerDisplayName ?? ''],
      ['Eksport dato', new Date().toISOString()],
    ];
    const csv = rows.map(r => r.map(v => `"${String(v).replace(/"/g, '""')}"`).join(',')).join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `lederanalyse-${new Date().toISOString().slice(0,10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  };

  const handlePdf = () => {
    window.print();
  };

  return (
    <section id="leader-analysis-export" className="leader-analysis-card" aria-labelledby="export-heading">
      <div className="leader-analysis-card__header">
        <h3 id="export-heading" style={{ display: 'flex', alignItems: 'center', gap: '8px', margin: 0 }}><BarChart3 size={18} aria-hidden="true" /> Eksport & deling til ledermøder</h3>
        <p>Del nøgletal som CSV eller PDF — klar til ledermødet.</p>
      </div>
      <div className="leader-analysis-card__body" style={{ display: 'flex', gap: '10px', flexWrap: 'wrap' }}>
        <button id="leader-analysis-export-csv" type="button" className="btn btn-secondary" onClick={handleCsv} style={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}>
          <WalletCards size={16} aria-hidden="true" /> Eksportér CSV
        </button>
        <button id="leader-analysis-export-pdf" type="button" className="btn btn-primary" onClick={handlePdf} style={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}>
          <ExternalLink size={16} aria-hidden="true" /> Eksportér PDF (Print)
        </button>
        <span style={{ fontSize: '12px', color: 'var(--muted)', alignSelf: 'center' }}>CSV indeholder KPI’er + økonomi. PDF bruger browserens print-dialog.</span>
      </div>
    </section>
  );
}
