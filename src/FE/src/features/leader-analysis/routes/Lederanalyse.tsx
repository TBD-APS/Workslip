import { useQuery } from '@tanstack/react-query';
import { AlertTriangle, ArrowRight, BarChart3, CheckCircle2, ClipboardList, Clock3, TrendingUp, Users, XCircle } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { ErrorState } from '../../../components/ErrorState';
import { formatDateTimeShort } from '../../../lib/formatDate';
import { fetchLeaderAnalysisSummary, leaderAnalysisQueryKey } from '../api';
import './Lederanalyse.css';

const formatPercent = (value: number | null) => {
  if (value === null) return '—';
  return `${Math.round(value * 100)} %`;
};

export function Lederanalyse() {
  const navigate = useNavigate();
  const query = useQuery({
    queryKey: leaderAnalysisQueryKey,
    queryFn: fetchLeaderAnalysisSummary,
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

      <section className="leader-analysis-card" aria-labelledby="leader-next-heading">
        <div className="leader-analysis-card__header">
          <h3 id="leader-next-heading">Næste skridt</h3>
          <p>Modulet er bevidst startet som et tyndt ledelsesoverblik oven på eksisterende data.</p>
        </div>
        <div className="leader-analysis-card__body">
          <ul style={{ margin: 0, paddingLeft: '1.1rem', display: 'grid', gap: '0.4rem' }}>
            <li>Bemanding & belægning (Folk + Timer) – reserveret til næste iteration</li>
            <li>Sagsøkonomi pr. sag/kunde (timer × sats, faktureringsgrad)</li>
            <li>Gennemløbstid og SLA-advarsler for sager til gennemsyn</li>
            <li>Eksport og deling til ledermøder (PDF/CSV)</li>
          </ul>
        </div>
      </section>
    </div>
  );
}
