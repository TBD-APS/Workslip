import { useQuery } from '@tanstack/react-query';
import {
  Activity,
  BadgeCheck,
  Clock3,
  RefreshCw,
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
        <small>{item.caseCount} sager · {item.approvedCount} godkendt</small>
      </td>
      <td>{formatSeconds(item.medianCaseCreationSeconds)}</td>
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

  return (
    <section className="superadmin-flow" aria-labelledby="superadmin-flow-title">
      <div className="superadmin-flow-header">
        <div>
          <div className="superadmin-flow-eyebrow">
            <Activity size={15} aria-hidden="true" /> Produktivitetsdata
          </div>
          <h2 id="superadmin-flow-title">Sags- og brugerflow</h2>
          <p>
            Mål faktisk tidsforbrug på tværs af Workslip: oprettelse, medarbejderens udfyldelse,
            godkendelse og samlet gennemløbstid. Denne analyse ligger kun i Superadmin.
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
          <span>Produktivitetsdata kunne ikke hentes.</span>
          <button type="button" className="btn btn-secondary" onClick={() => { void query.refetch(); }}>Prøv igen</button>
        </div>
      ) : (
        <>
          <div className="superadmin-flow-kpis" aria-live="polite">
            <article className="superadmin-flow-kpi">
              <span className="superadmin-flow-kpi-icon"><Timer size={18} aria-hidden="true" /></span>
              <div>
                <span className="superadmin-flow-kpi-label">Opret en sag</span>
                <strong>{query.isPending ? '—' : formatSeconds(totals?.medianCaseCreationSeconds ?? null)}</strong>
                <small>P90 {formatSeconds(totals?.p90CaseCreationSeconds ?? null)}</small>
                <Coverage sample={totals?.creationSampleSize ?? 0} total={Math.max(totals?.caseCount ?? 0, 1)} />
              </div>
            </article>

            <article className="superadmin-flow-kpi">
              <span className="superadmin-flow-kpi-icon"><UsersRound size={18} aria-hidden="true" /></span>
              <div>
                <span className="superadmin-flow-kpi-label">Oprettet → medarbejder starter</span>
                <strong>{query.isPending ? '—' : formatHours(totals?.medianCreationToFirstOpenHours ?? null)}</strong>
                <small>P90 {formatHours(totals?.p90CreationToFirstOpenHours ?? null)}</small>
                <Coverage sample={totals?.firstOpenSampleSize ?? 0} total={Math.max(totals?.caseCount ?? 0, 1)} />
              </div>
            </article>

            <article className="superadmin-flow-kpi">
              <span className="superadmin-flow-kpi-icon"><Clock3 size={18} aria-hidden="true" /></span>
              <div>
                <span className="superadmin-flow-kpi-label">Medarbejder udfylder</span>
                <strong>{query.isPending ? '—' : formatMinutes(totals?.medianEmployeeFillMinutes ?? null)}</strong>
                <small>P90 {formatMinutes(totals?.p90EmployeeFillMinutes ?? null)}</small>
                <Coverage sample={totals?.employeeFillSampleSize ?? 0} total={Math.max(totals?.caseCount ?? 0, 1)} />
              </div>
            </article>

            <article className="superadmin-flow-kpi superadmin-flow-kpi--primary">
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
              <span>Indsendt → godkendt</span>
              <strong>{formatHours(totals?.medianSubmitToApprovalHours ?? null)}</strong>
              <small>P90 {formatHours(totals?.p90SubmitToApprovalHours ?? null)}</small>
            </div>
            <div className="superadmin-flow-stat">
              <span>Starter inden 24 timer</span>
              <strong>{formatPercent(totals?.startedWithin24HoursRate ?? null)}</strong>
              <small>Fra oprettelse til første åbning</small>
            </div>
            <div className="superadmin-flow-stat">
              <span>Afsluttet inden 1 døgn</span>
              <strong>{formatPercent(totals?.completedWithinOneDayRate ?? null)}</strong>
              <small>Oprettet → godkendt</small>
            </div>
            <div className="superadmin-flow-stat">
              <span>Godkendte / 30 dage</span>
              <strong>{formatNumber(totals?.approvedPer30Days ?? 0)}</strong>
              <small>Normaliseret throughput</small>
            </div>
          </div>

          <div className="superadmin-flow-quality">
            <div>
              <BadgeCheck size={18} aria-hidden="true" />
              <span>Godkendt første gang</span>
              <strong>{formatPercent(totals?.firstPassApprovalRate ?? null)}</strong>
            </div>
            <div>
              <XCircle size={18} aria-hidden="true" />
              <span>Sager afvist mindst én gang</span>
              <strong>{totals?.rejectedCaseCount ?? 0}</strong>
            </div>
            <div>
              <Activity size={18} aria-hidden="true" />
              <span>Afvisninger / godkendt sag</span>
              <strong>{totals?.rejectionsPerApprovedCase === null || totals?.rejectionsPerApprovedCase === undefined
                ? '—'
                : formatNumber(totals.rejectionsPerApprovedCase, 2)}</strong>
            </div>
          </div>

          <div className="superadmin-flow-evidence">
            <TrendingUp size={20} aria-hidden="true" />
            <div>
              <strong>Datagrundlag til at dokumentere effektivisering</strong>
              <p>
                Workslip måler nu den digitale proces. Brug median, P90, first-pass approval og gennemløbstid som før/efter-målinger.
                En påstand om besparelse mod manuelt arbejde bør først vises, når der findes en reel manuel baseline fra samme arbejdsgang.
              </p>
            </div>
          </div>

          <div className="superadmin-flow-table-wrap">
            <div className="superadmin-flow-table-heading">
              <div>
                <h3>Organisationer</h3>
                <p>Sammenlign hvor flowet er hurtigt, og hvor der opstår ventetid.</p>
              </div>
              <span>{query.data?.organizations.length ?? 0} vist</span>
            </div>
            <div className="superadmin-flow-table-scroll">
              <table className="superadmin-flow-table">
                <thead>
                  <tr>
                    <th>Organisation</th>
                    <th>Opret sag</th>
                    <th>Til start</th>
                    <th>Udfyldelse</th>
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
