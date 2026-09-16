import { useQuery } from '@tanstack/react-query';
import {
  Activity,
  BadgeCheck,
  Clock3,
  RefreshCw,
  Send,
  Timer,
  TrendingUp,
  UsersRound,
  XCircle,
} from 'lucide-react';
import { useState } from 'react';
import {
  getSuperadminCaseFlowAnalytics,
  superadminCaseFlowAnalyticsQueryKey,
  type SuperAdminCaseFlowOrganizationSummary,
} from '../api';
import type { Organization } from '../types';
import './SuperAdminProductivityAnalytics.css';

type Props = {
  organizations: Organization[];
};

const dayOptions = [30, 90, 180, 365] as const;

const formatNumber = (value: number, maximumFractionDigits = 1) =>
  new Intl.NumberFormat('da-DK', { maximumFractionDigits }).format(value);

const formatPercent = (value: number | null) =>
  value === null ? '—' : `${Math.round(value * 100)} %`;

const formatSeconds = (value: number | null) => {
  if (value === null) return '—';
  if (value < 60) return `${Math.round(value)} sek`;
  if (value < 3600) return `${formatNumber(value / 60)} min`;
  return `${formatNumber(value / 3600)} t`;
};

const formatMinutes = (value: number | null) => {
  if (value === null) return '—';
  if (value < 60) return `${formatNumber(value)} min`;
  if (value < 1440) return `${formatNumber(value / 60)} t`;
  return `${formatNumber(value / 1440)} dage`;
};

const formatHours = (value: number | null) => {
  if (value === null) return '—';
  if (value < 1) return `${Math.round(value * 60)} min`;
  if (value < 48) return `${formatNumber(value)} t`;
  return `${formatNumber(value / 24)} dage`;
};

const formatDays = (value: number | null) =>
  value === null ? '—' : `${formatNumber(value)} dage`;

function Coverage({ sample, total }: { sample: number; total: number }) {
  const percentage = total > 0 ? Math.min(100, Math.round((sample / total) * 100)) : 0;
  return (
    <span className="superadmin-flow-coverage" title={`${sample} målinger ud af ${total} relevante sager`}>
      {sample} målinger · {percentage}% dækning
    </span>
  );
}

function OrganizationRow({ item }: { item: SuperAdminCaseFlowOrganizationSummary }) {
  return (
    <tr>
      <td>
        <strong>{item.organizationName}</strong>
        <small>{item.caseCount} sager · {item.submittedCount} indsendt · {item.approvedCount} godkendt</small>
      </td>
      <td>{formatHours(item.medianCreationToSubmissionHours)}</td>
      <td>{formatHours(item.medianCreationToFirstOpenHours)}</td>
      <td>{formatMinutes(item.medianEmployeeFillMinutes)}</td>
      <td>{formatDays(item.medianCreationToApprovalDays)}</td>
      <td>{formatPercent(item.firstPassApprovalRate)}</td>
    </tr>
  );
}

export function SuperAdminProductivityAnalytics({ organizations }: Props) {
  const [days, setDays] = useState<number>(90);
  const [organizationId, setOrganizationId] = useState('');

  const query = useQuery({
    queryKey: superadminCaseFlowAnalyticsQueryKey(days, organizationId || undefined),
    queryFn: () => getSuperadminCaseFlowAnalytics({
      days,
      organizationId: organizationId || undefined,
    }),
    refetchInterval: 60_000,
  });

  const totals = query.data?.totals;
  const hasCases = (totals?.caseCount ?? 0) > 0;

  return (
    <section className="superadmin-flow" aria-labelledby="superadmin-flow-title">
      <div className="superadmin-flow-header">
        <div>
          <div className="superadmin-flow-eyebrow">
            <Activity size={15} aria-hidden="true" /> Live Workslip-data
          </div>
          <h2 id="superadmin-flow-title">Sagsflow og dokumenteret kundeværdi</h2>
          <p>
            Tallene beregnes direkte fra eksisterende Workslip-sager, indsendelser, visninger,
            status-historik og godkendelser. Brug median og P90 til at dokumentere det faktiske flow.
          </p>
        </div>

        <div className="superadmin-flow-filters" aria-label="Filtre for produktivitetsdata">
          <label>
            <span>Organisation</span>
            <select value={organizationId} onChange={(event) => setOrganizationId(event.target.value)}>
              <option value="">Alle organisationer</option>
              {organizations.map((organization) => (
                <option key={organization.id} value={organization.id}>{organization.name}</option>
              ))}
            </select>
          </label>
          <label>
            <span>Periode</span>
            <select value={days} onChange={(event) => setDays(Number(event.target.value))}>
              {dayOptions.map((option) => (
                <option key={option} value={option}>Seneste {option} dage</option>
              ))}
            </select>
          </label>
          <button
            type="button"
            className="btn btn-secondary superadmin-flow-refresh"
            onClick={() => { void query.refetch(); }}
            disabled={query.isFetching}
          >
            <RefreshCw size={15} className={query.isFetching ? 'animate-spin' : undefined} aria-hidden="true" />
            Opdatér
          </button>
        </div>
      </div>

      {query.isError ? (
        <div className="superadmin-alert superadmin-alert-error" role="alert">
          <span>Produktivitetsdata kunne ikke hentes fra Workslip-databasen.</span>
          <button type="button" className="btn btn-secondary" onClick={() => { void query.refetch(); }}>Prøv igen</button>
        </div>
      ) : (
        <>
          <div className="superadmin-flow-quality" aria-live="polite">
            <div>
              <Activity size={18} aria-hidden="true" />
              <span>Sager i perioden</span>
              <strong>{query.isPending ? '—' : formatNumber(totals?.caseCount ?? 0, 0)}</strong>
            </div>
            <div>
              <Send size={18} aria-hidden="true" />
              <span>Indsendte sager</span>
              <strong>{query.isPending ? '—' : formatNumber(totals?.submittedCount ?? 0, 0)}</strong>
            </div>
            <div>
              <BadgeCheck size={18} aria-hidden="true" />
              <span>Godkendte sager</span>
              <strong>{query.isPending ? '—' : formatNumber(totals?.approvedCount ?? 0, 0)}</strong>
            </div>
            <div>
              <XCircle size={18} aria-hidden="true" />
              <span>Afvist mindst én gang</span>
              <strong>{query.isPending ? '—' : formatNumber(totals?.rejectedCaseCount ?? 0, 0)}</strong>
            </div>
          </div>

          <div className="superadmin-flow-kpis" aria-live="polite">
            <article className="superadmin-flow-kpi superadmin-flow-kpi--primary">
              <span className="superadmin-flow-kpi-icon"><Send size={18} aria-hidden="true" /></span>
              <div>
                <span className="superadmin-flow-kpi-label">Oprettet → medarbejder indsender</span>
                <strong>{query.isPending ? '—' : formatHours(totals?.medianCreationToSubmissionHours ?? null)}</strong>
                <small>P90 {formatHours(totals?.p90CreationToSubmissionHours ?? null)}</small>
                <Coverage sample={totals?.creationToSubmissionSampleSize ?? 0} total={Math.max(totals?.caseCount ?? 0, 1)} />
              </div>
            </article>

            <article className="superadmin-flow-kpi">
              <span className="superadmin-flow-kpi-icon"><UsersRound size={18} aria-hidden="true" /></span>
              <div>
                <span className="superadmin-flow-kpi-label">Oprettet → medarbejder åbner</span>
                <strong>{query.isPending ? '—' : formatHours(totals?.medianCreationToFirstOpenHours ?? null)}</strong>
                <small>P90 {formatHours(totals?.p90CreationToFirstOpenHours ?? null)}</small>
                <Coverage sample={totals?.firstOpenSampleSize ?? 0} total={Math.max(totals?.caseCount ?? 0, 1)} />
              </div>
            </article>

            <article className="superadmin-flow-kpi">
              <span className="superadmin-flow-kpi-icon"><Clock3 size={18} aria-hidden="true" /></span>
              <div>
                <span className="superadmin-flow-kpi-label">Åbner → indsender</span>
                <strong>{query.isPending ? '—' : formatMinutes(totals?.medianEmployeeFillMinutes ?? null)}</strong>
                <small>P90 {formatMinutes(totals?.p90EmployeeFillMinutes ?? null)}</small>
                <Coverage sample={totals?.employeeFillSampleSize ?? 0} total={Math.max(totals?.submittedCount ?? 0, 1)} />
              </div>
            </article>

            <article className="superadmin-flow-kpi">
              <span className="superadmin-flow-kpi-icon"><TrendingUp size={18} aria-hidden="true" /></span>
              <div>
                <span className="superadmin-flow-kpi-label">Oprettet → godkendt</span>
                <strong>{query.isPending ? '—' : formatDays(totals?.medianCreationToApprovalDays ?? null)}</strong>
                <small>P90 {formatDays(totals?.p90CreationToApprovalDays ?? null)}</small>
                <Coverage sample={totals?.cycleSampleSize ?? 0} total={Math.max(totals?.approvedCount ?? 0, 1)} />
              </div>
            </article>
          </div>

          <div className="superadmin-flow-secondary">
            <div className="superadmin-flow-stat">
              <span>Indsendt inden 24 timer</span>
              <strong>{formatPercent(totals?.submittedWithin24HoursRate ?? null)}</strong>
              <small>Fra oprettelse til medarbejderens indsendelse</small>
            </div>
            <div className="superadmin-flow-stat">
              <span>Indsendt → godkendt</span>
              <strong>{formatHours(totals?.medianSubmitToApprovalHours ?? null)}</strong>
              <small>P90 {formatHours(totals?.p90SubmitToApprovalHours ?? null)}</small>
            </div>
            <div className="superadmin-flow-stat">
              <span>Godkendt første gang</span>
              <strong>{formatPercent(totals?.firstPassApprovalRate ?? null)}</strong>
              <small>Andel uden tidligere afvisning</small>
            </div>
            <div className="superadmin-flow-stat">
              <span>Afvisninger / godkendt sag</span>
              <strong>{totals?.rejectionsPerApprovedCase === null || totals?.rejectionsPerApprovedCase === undefined
                ? '—'
                : formatNumber(totals.rejectionsPerApprovedCase, 2)}</strong>
              <small>{totals?.rejectionEventCount ?? 0} registrerede afvisninger</small>
            </div>
          </div>

          <div className="superadmin-flow-evidence">
            <TrendingUp size={20} aria-hidden="true" />
            <div>
              <strong>Tal der kan bruges i salg og kundedialog</strong>
              <p>
                Eksempel: “Medianen fra en sag oprettes til medarbejderen indsender er {formatHours(totals?.medianCreationToSubmissionHours ?? null)},
                og {formatPercent(totals?.submittedWithin24HoursRate ?? null)} bliver indsendt inden 24 timer.”
                Tallene kommer fra de valgte Workslip-sager og opdateres løbende.
              </p>
            </div>
          </div>

          <div className="superadmin-flow-secondary">
            <div className="superadmin-flow-stat">
              <span>Opret en sag i Workslip</span>
              <strong>{formatSeconds(totals?.medianCaseCreationSeconds ?? null)}</strong>
              <small>
                {totals?.creationSampleSize ?? 0} nye målinger · fremadrettet telemetry
              </small>
            </div>
            <div className="superadmin-flow-stat">
              <span>Starter inden 24 timer</span>
              <strong>{formatPercent(totals?.startedWithin24HoursRate ?? null)}</strong>
              <small>Baseret på registreret første åbning</small>
            </div>
            <div className="superadmin-flow-stat">
              <span>Godkendt inden 1 døgn</span>
              <strong>{formatPercent(totals?.completedWithinOneDayRate ?? null)}</strong>
              <small>Oprettet → endelig godkendelse</small>
            </div>
            <div className="superadmin-flow-stat">
              <span>Godkendte / 30 dage</span>
              <strong>{formatNumber(totals?.approvedPer30Days ?? 0)}</strong>
              <small>Normaliseret throughput</small>
            </div>
          </div>

          {!query.isPending && !hasCases && (
            <div className="superadmin-alert" role="status">
              Der findes ingen ikke-slettede Workslip-sager i den valgte periode. Vælg en længere periode.
            </div>
          )}

          <div className="superadmin-flow-table-wrap">
            <div className="superadmin-flow-table-heading">
              <div>
                <h3>Organisationer</h3>
                <p>Sammenlign faktiske sagsflows og se, hvor ventetiden opstår.</p>
              </div>
              <span>{query.data?.organizations.length ?? 0} vist</span>
            </div>
            <div className="superadmin-flow-table-scroll">
              <table className="superadmin-flow-table">
                <thead>
                  <tr>
                    <th>Organisation</th>
                    <th>Oprettet → indsendt</th>
                    <th>Til første åbning</th>
                    <th>Åbning → indsendt</th>
                    <th>Til godkendt</th>
                    <th>First-pass</th>
                  </tr>
                </thead>
                <tbody>
                  {(query.data?.organizations ?? []).map((item) => (
                    <OrganizationRow key={item.organizationId} item={item} />
                  ))}
                  {!query.isPending && (query.data?.organizations.length ?? 0) === 0 && (
                    <tr>
                      <td colSpan={6} className="superadmin-flow-table-empty">Ingen sager i den valgte periode.</td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}
    </section>
  );
}
