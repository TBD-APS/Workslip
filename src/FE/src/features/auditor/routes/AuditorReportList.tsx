import { useDeferredValue, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ChevronRight, ClipboardCheck, MapPin, ShieldCheck } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { ErrorState } from '../../../components/ErrorState';
import { SearchBar } from '../../../components/filters/SearchBar';
import { formatDateLong } from '../../../lib/formatDate';
import { getAuditorOrganizations, getAuditorReports } from '../api';
import '../auditor.css';

const PAGE_SIZE = 100;

type InstallationFilter = '' | 'Vand' | 'Afløb';

export const AuditorReportList = () => {
  const navigate = useNavigate();
  const [requestedOrganizationId, setRequestedOrganizationId] = useState<string>('');
  const [search, setSearch] = useState('');
  const deferredSearch = useDeferredValue(search.trim());
  const [installationType, setInstallationType] = useState<InstallationFilter>('');

  const organizationsQuery = useQuery({
    queryKey: ['auditor-organizations'],
    queryFn: getAuditorOrganizations,
  });

  const organizations = organizationsQuery.data ?? [];
  const selectedOrganizationId = organizations.some(
    (organization) => organization.organizationId === requestedOrganizationId,
  )
    ? requestedOrganizationId
    : (organizations[0]?.organizationId ?? '');

  const reportsQuery = useQuery({
    queryKey: ['auditor-reports', selectedOrganizationId, deferredSearch, installationType],
    queryFn: () => getAuditorReports(selectedOrganizationId, {
      search: deferredSearch || undefined,
      installationType: installationType || undefined,
      limit: PAGE_SIZE,
      offset: 0,
    }),
    enabled: Boolean(selectedOrganizationId),
  });

  const selectedOrganization = organizations.find(
    (organization) => organization.organizationId === selectedOrganizationId,
  );

  if (organizationsQuery.isLoading) {
    return <div className="page-container"><div className="auditor-empty">Henter auditor-adgang…</div></div>;
  }

  if (organizationsQuery.isError) {
    return (
      <div className="page-container">
        <ErrorState message="Kunne ikke hente dine auditor-tilknytninger." onRetry={() => void organizationsQuery.refetch()} />
      </div>
    );
  }

  const reports = reportsQuery.data?.items ?? [];

  return (
    <div className="page-container auditor-page" data-testid="auditor-workspace">
      <header className="auditor-page__intro">
        <div>
          <p className="auditor-page__eyebrow">Ekstern KLS-gennemgang</p>
          <h1>Audit</h1>
          <p className="auditor-page__subtitle">
            Vælg en virksomhed, find en stikprøvesag og følg dokumentationen fra udførelse til kontrol og opfølgning.
          </p>
        </div>
        <span className="auditor-count" aria-label={`${organizations.length} virksomheder`}>
          {organizations.length}
        </span>
      </header>

      {organizations.length === 0 ? (
        <section className="auditor-section auditor-empty" data-testid="auditor-no-assignments">
          <ShieldCheck size={28} aria-hidden="true" />
          <h2>Ingen aktive tilknytninger</h2>
          <p>Du kan først se en virksomhed, når en SuperAdmin har oprettet en aktiv VVS-auditor-tilknytning.</p>
        </section>
      ) : (
        <>
          <section className="auditor-section" aria-labelledby="auditor-organizations-heading">
            <div className="auditor-section__header">
              <div>
                <p className="auditor-section__eyebrow">Adgang</p>
                <h2 id="auditor-organizations-heading">Virksomheder</h2>
              </div>
            </div>
            <div className="auditor-organizations">
              {organizations.map((organization) => (
                <button
                  key={organization.assignmentId}
                  type="button"
                  className="auditor-organization-card"
                  aria-pressed={organization.organizationId === selectedOrganizationId}
                  onClick={() => setRequestedOrganizationId(organization.organizationId)}
                  data-testid={`auditor-organization-${organization.organizationId}`}
                >
                  <span className="auditor-organization-card__header">
                    <span className="auditor-organization-card__name">{organization.organizationName}</span>
                    <span className="auditor-count" title="Åbne fund">{organization.openFindings}</span>
                  </span>
                  <span className="auditor-organization-card__meta">
                    CVR {organization.cvr} · {organization.authorizationArea}
                  </span>
                  <span className="auditor-organization-card__meta">
                    Aktiv fra {formatDateLong(organization.activeFrom) ?? '—'}
                    {organization.activeUntil ? ` til ${formatDateLong(organization.activeUntil) ?? '—'}` : ''}
                  </span>
                </button>
              ))}
            </div>
          </section>

          <section className="auditor-section" aria-labelledby="auditor-reports-heading">
            <div className="auditor-section__header">
              <div>
                <p className="auditor-section__eyebrow">Stikprøver</p>
                <h2 id="auditor-reports-heading">{selectedOrganization?.organizationName ?? 'Rapporter'}</h2>
                <p className="auditor-muted">Kun godkendte sager i auditor-scope med Vand eller Afløb vises.</p>
              </div>
              <span className="auditor-count" aria-label={`${reportsQuery.data?.totalCount ?? 0} sager`}>
                {reportsQuery.data?.totalCount ?? 0}
              </span>
            </div>

            <div className="auditor-filter-row">
              <SearchBar value={search} onChange={setSearch} placeholder="Søg sagsnr., kunde eller adresse…" />
              <div className="auditor-filter-group" aria-label="Filtrer på anlægstype">
                {([
                  ['', 'Alle'],
                  ['Vand', 'Vand'],
                  ['Afløb', 'Afløb'],
                ] as const).map(([value, label]) => (
                  <button
                    key={label}
                    type="button"
                    className="auditor-filter-button"
                    aria-pressed={installationType === value}
                    onClick={() => setInstallationType(value)}
                  >
                    {label}
                  </button>
                ))}
              </div>
            </div>

            {reportsQuery.isLoading ? (
              <div className="auditor-empty">Henter sager…</div>
            ) : reportsQuery.isError ? (
              <ErrorState message="Kunne ikke hente auditor-sager." onRetry={() => void reportsQuery.refetch()} />
            ) : reports.length === 0 ? (
              <div className="auditor-empty" data-testid="auditor-no-reports">
                <ClipboardCheck size={28} aria-hidden="true" />
                <p>Ingen sager matcher den valgte virksomhed og filtrering.</p>
              </div>
            ) : (
              <>
                <table className="auditor-report-table" data-testid="auditor-report-table">
                  <thead>
                    <tr>
                      <th>Sagsnr.</th>
                      <th>Kunde / adresse</th>
                      <th>Anlæg</th>
                      <th>Udført af</th>
                      <th>Timer</th>
                      <th>Rapportdato</th>
                      <th>Åbne fund</th>
                      <th aria-label="Åbn" />
                    </tr>
                  </thead>
                  <tbody>
                    {reports.map((report) => (
                      <tr
                        key={report.id}
                        tabIndex={0}
                        onClick={() => navigate(`/app/auditor/${selectedOrganizationId}/reports/${report.id}`)}
                        onKeyDown={(event) => {
                          if (event.key === 'Enter' || event.key === ' ') {
                            event.preventDefault();
                            navigate(`/app/auditor/${selectedOrganizationId}/reports/${report.id}`);
                          }
                        }}
                        data-testid={`auditor-report-${report.id}`}
                      >
                        <td><strong>SAG-{(report.reportNumber || report.id.slice(0, 4)).toUpperCase()}</strong></td>
                        <td>
                          <div>{report.customerName || 'Ukendt kunde'}</div>
                          <div className="auditor-muted"><MapPin size={13} aria-hidden="true" /> {report.address || 'Adresse ikke angivet'}</div>
                        </td>
                        <td>{report.installationTypes || '—'}</td>
                        <td>{report.assignedUsers || '—'}</td>
                        <td>{report.totalHours ?? '—'}</td>
                        <td>{formatDateLong(report.reportDate) ?? '—'}</td>
                        <td>{report.openFindings}</td>
                        <td><ChevronRight size={16} aria-hidden="true" /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>

                <div className="auditor-report-cards" data-testid="auditor-report-cards">
                  {reports.map((report) => (
                    <article
                      key={report.id}
                      className="auditor-report-card"
                      role="button"
                      tabIndex={0}
                      onClick={() => navigate(`/app/auditor/${selectedOrganizationId}/reports/${report.id}`)}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter' || event.key === ' ') {
                          event.preventDefault();
                          navigate(`/app/auditor/${selectedOrganizationId}/reports/${report.id}`);
                        }
                      }}
                    >
                      <div className="auditor-report-card__top">
                        <strong>SAG-{(report.reportNumber || report.id.slice(0, 4)).toUpperCase()}</strong>
                        <span className="auditor-count" title="Åbne fund">{report.openFindings}</span>
                      </div>
                      <h3>{report.customerName || 'Ukendt kunde'}</h3>
                      <p className="auditor-muted">{report.address || 'Adresse ikke angivet'}</p>
                      <div className="auditor-report-card__meta">
                        <span>{report.installationTypes || '—'}</span>
                        <span>{report.totalHours != null ? `${report.totalHours} t` : 'Timer —'}</span>
                        <span>{formatDateLong(report.reportDate) ?? 'Ingen rapportdato'}</span>
                      </div>
                    </article>
                  ))}
                </div>
              </>
            )}
          </section>
        </>
      )}
    </div>
  );
};
