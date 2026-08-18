import { useMemo, useRef, useState, type ReactNode } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  ArrowLeft,
  CalendarDays,
  Clock3,
  Download,
  FileText,
  History,
  Loader2,
  MapPin,
  Pencil,
  User,
  Users,
} from 'lucide-react';
import { useGetApiJobsId, useGetApiJobsIdHistory } from '../../../api/generated/jobs/jobs';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import type { JobHistoryResponse } from '../../../api/generated/models';
import { ErrorState } from '../../../components/ErrorState';
import { formatDateLong, formatDateTime } from '../../../lib/formatDate';
import { notify } from '../../../lib/toast';
import { JobConversationLauncher } from '../components/JobConversationLauncher';
import { JobStatusDots } from '../components/JobStatusDots';
import { formatJobStatus } from '../statusLabels';
import { downloadJobReportPdf } from '../utils/downloadJobReportPdf';
import { formatInstallationTypeNames, formatReportNumber } from '../utils/completedJobFormatters';
import './AdminCompletedJobReport.css';

type JobEntryLocationState = {
  from?: string;
  readOnly?: boolean;
};

type TimelineTone = 'danger' | 'warning' | 'success' | 'info' | 'neutral';

export function AdminCompletedJobReport() {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const navigate = useNavigate();
  const state = (location.state as JobEntryLocationState | null) ?? undefined;
  const from = state?.from ?? '/app';
  const readOnly = Boolean(state?.readOnly);
  const historyPanelRef = useRef<HTMLElement | null>(null);
  const [isDownloading, setIsDownloading] = useState(false);

  const jobQuery = useGetApiJobsId(id ?? '', {
    query: { enabled: Boolean(id) },
  });
  const historyQuery = useGetApiJobsIdHistory(id ?? '', undefined, {
    query: { enabled: Boolean(id) },
  });

  const job = jobQuery.data;
  const history = useMemo(
    () => [...(historyQuery.data ?? [])].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt)),
    [historyQuery.data],
  );

  const createdAt = history.length > 0 ? history[history.length - 1]?.createdAt : undefined;
  const updatedAt = history[0]?.createdAt;

  if (!id) return null;
  if (jobQuery.isLoading) return <AdminReferenceSkeleton />;

  if (jobQuery.isError || !job) {
    return (
      <div className="page-container admin-case-reference-page">
        <ErrorState message="Kunne ikke hente sagen." onRetry={() => jobQuery.refetch()} />
      </div>
    );
  }

  const title = job.observations.taskDescription?.trim() || 'Sagsoverblik';
  const installationLabel = formatInstallationTypeNames(job.work.installationTypes) || job.jobType;
  const assigneeLabel = job.assignedUsers.length > 0
    ? job.assignedUsers.map((assignedUser) => assignedUser.displayName).join(', ')
    : 'Ikke tildelt';
  const address = job.destinationAddress || job.customerSnapshot.address || 'Ikke angivet';

  const handleDownload = async () => {
    setIsDownloading(true);
    try {
      await downloadJobReportPdf(job);
    } catch {
      notify.error(`Kunne ikke hente PDF for ${formatReportNumber(job)}`);
    } finally {
      setIsDownloading(false);
    }
  };

  const handleEdit = () => {
    navigate(`/app/job/${job.id}`, {
      state: { from: location.pathname, forceEdit: true },
    });
  };

  const handleHistoryFocus = () => {
    historyPanelRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    historyPanelRef.current?.focus({ preventScroll: true });
  };

  return (
    <div className="page-container admin-case-reference-page">
      <button type="button" className="admin-case-reference-back" onClick={() => navigate(from, { replace: true })}>
        <ArrowLeft size={17} aria-hidden="true" />
        Tilbage til sager
      </button>

      <div className="admin-case-reference-layout">
        <main className="admin-case-reference-main">
          <header className="admin-case-reference-hero">
            <div className="admin-case-reference-heading">
              <span className="admin-case-reference-number">
                {formatReportNumber(job)} - {formatJobStatus(job.status)}
              </span>
              <h1>{title}</h1>
              <div className="admin-case-reference-meta" aria-label="Sagsmetadata">
                <span className={`admin-case-reference-status ${statusToneClass(job.status)}`}>
                  <span aria-hidden="true">{statusGlyph(job.status)}</span>
                  {formatJobStatus(job.status)}
                </span>
                <span className="admin-case-reference-meta-separator" aria-hidden="true">•</span>
                <span>{installationLabel}</span>
                {updatedAt && (
                  <>
                    <span className="admin-case-reference-meta-separator" aria-hidden="true">•</span>
                    <Clock3 size={15} aria-hidden="true" />
                    <span>{formatDateLong(updatedAt) ?? 'Ukendt dato'}</span>
                  </>
                )}
              </div>
            </div>
            <div className="admin-case-reference-progress" aria-label="Sagsstatus">
              <JobStatusDots status={job.status} enabledStatuses={[]} />
            </div>
          </header>

          <div className="admin-case-reference-actions" aria-label="Sagshandlinger">
            {!readOnly && job.status !== JobStatus.Approved && (
              <button type="button" className="admin-case-reference-action" onClick={handleEdit}>
                <Pencil size={23} aria-hidden="true" />
                <span>Rediger</span>
              </button>
            )}
            <JobConversationLauncher
              jobId={job.id}
              allowSubmitForReview={job.status === JobStatus.Draft || job.status === JobStatus.Rejected}
              className="admin-case-reference-action"
            />
            <button type="button" className="admin-case-reference-action" onClick={handleHistoryFocus}>
              <History size={23} aria-hidden="true" />
              <span>Historik</span>
            </button>
            <button
              type="button"
              className="admin-case-reference-action"
              onClick={() => void handleDownload()}
              disabled={isDownloading}
            >
              {isDownloading ? <Loader2 className="spin" size={23} aria-hidden="true" /> : <Download size={23} aria-hidden="true" />}
              <span>Download</span>
            </button>
          </div>

          <section className="admin-case-reference-card admin-case-reference-information" aria-labelledby="admin-case-information-title">
            <h2 id="admin-case-information-title">Sagsinformation</h2>
            <dl>
              <InfoRow icon={<Users size={18} />} label="Kunde" value={job.customerSnapshot.name || 'Ikke angivet'} />
              <InfoRow icon={<MapPin size={18} />} label="Adresse" value={address} />
              <InfoRow icon={<User size={18} />} label="Sagsansvarlig" value={assigneeLabel} />
              <InfoRow icon={<CalendarDays size={18} />} label="Oprettet" value={createdAt ? (formatDateLong(createdAt) ?? 'Ikke registreret') : 'Ikke registreret'} />
              <InfoRow icon={<Clock3 size={18} />} label="Senest opdateret" value={updatedAt ? (formatDateTime(updatedAt) ?? 'Ikke registreret') : 'Ikke registreret'} />
            </dl>
          </section>

          <section className="admin-case-reference-card admin-case-reference-description" aria-labelledby="admin-case-description-title">
            <h2 id="admin-case-description-title">Beskrivelse</h2>
            <p>{job.observations.taskDescription?.trim() || 'Der er ikke tilføjet en beskrivelse til sagen.'}</p>

            {(job.observations.customerObservations?.trim() || job.observations.technicalObservations?.trim()) && (
              <div className="admin-case-reference-notes">
                {job.observations.customerObservations?.trim() && (
                  <div>
                    <strong>Oplysninger til kunden</strong>
                    <p>{job.observations.customerObservations.trim()}</p>
                  </div>
                )}
                {job.observations.technicalObservations?.trim() && (
                  <div>
                    <strong>Kommentar til sagen</strong>
                    <p>{job.observations.technicalObservations.trim()}</p>
                  </div>
                )}
              </div>
            )}

            <div className="admin-case-reference-document-note">
              <FileText size={18} aria-hidden="true" />
              <span>Dokumentation og sagsfiler bevares i Workslips eksisterende dokumentationsflow.</span>
            </div>
          </section>
        </main>

        <aside className="admin-case-reference-sidebar" aria-label="Sagshistorik og lederkommentar">
          <section
            ref={historyPanelRef}
            tabIndex={-1}
            className="admin-case-reference-card admin-case-reference-history"
            aria-labelledby="admin-case-history-title"
          >
            <h2 id="admin-case-history-title">Sagshistorik</h2>
            {historyQuery.isLoading ? (
              <div className="admin-case-reference-history-loading">
                <Loader2 className="spin" size={20} />
                Henter historik...
              </div>
            ) : history.length === 0 ? (
              <p className="admin-case-reference-history-empty">Der er endnu ingen registreret historik.</p>
            ) : (
              <div className="admin-case-reference-timeline">
                {history.slice(0, 8).map((event) => <TimelineEvent key={event.id} event={event} />)}
              </div>
            )}
          </section>

          {job.rejectionNote?.trim() && (
            <section className="admin-case-reference-card admin-case-reference-manager-note" aria-labelledby="admin-case-manager-note-title">
              <h2 id="admin-case-manager-note-title">Kommentar fra leder</h2>
              <p>{job.rejectionNote.trim()}</p>
              <strong>{history[0]?.actorName?.trim() || 'Admin'}</strong>
              {updatedAt && <time dateTime={updatedAt}>{formatDateTime(updatedAt)}</time>}
            </section>
          )}
        </aside>
      </div>
    </div>
  );
}

function InfoRow({ icon, label, value }: { icon: ReactNode; label: string; value: string }) {
  return (
    <div className="admin-case-reference-info-row">
      <dt>
        <span className="admin-case-reference-info-icon" aria-hidden="true">{icon}</span>
        {label}
      </dt>
      <dd>{value}</dd>
    </div>
  );
}

function TimelineEvent({ event }: { event: JobHistoryResponse }) {
  const tone = timelineTone(event);
  return (
    <article className={`admin-case-reference-timeline-event admin-case-reference-timeline-event--${tone}`}>
      <span className="admin-case-reference-timeline-marker" aria-hidden="true">{timelineGlyph(tone)}</span>
      <div>
        <h3>{timelineTitle(event)}</h3>
        <time dateTime={event.createdAt}>{formatDateTime(event.createdAt)}</time>
        <strong>{event.actorName?.trim() || 'Workslip'}</strong>
        <p>{event.summary?.trim() || 'Sagen blev opdateret.'}</p>
      </div>
    </article>
  );
}

function timelineTone(event: JobHistoryResponse): TimelineTone {
  const haystack = [
    event.summary,
    event.eventType,
    ...event.changes.flatMap((change) => [change.before, change.after, change.displayName, change.propertyName]),
  ].filter(Boolean).join(' ').toLocaleLowerCase('da-DK');

  if (haystack.includes('afvist') || haystack.includes('rejected') || haystack.includes('slettet')) return 'danger';
  if (haystack.includes('gennemført') || haystack.includes('godkendt') || haystack.includes('approved')) return 'success';
  if (haystack.includes('afventer') || haystack.includes('gennemsyn') || haystack.includes('inreview')) return 'warning';
  if (haystack.includes('genåbnet') || haystack.includes('reopened')) return 'neutral';
  return 'info';
}

function timelineTitle(event: JobHistoryResponse) {
  const tone = timelineTone(event);
  if (tone === 'danger') return 'Afvist';
  if (tone === 'success') return 'Gennemført';
  if (tone === 'warning') return 'Afventer';
  if (tone === 'neutral') return 'Genåbnet';
  if (event.eventType.toLocaleLowerCase('da-DK') === 'added') return 'Oprettet';
  return 'Opdateret';
}

function timelineGlyph(tone: TimelineTone) {
  if (tone === 'danger') return '×';
  if (tone === 'success') return '✓';
  if (tone === 'warning') return 'Ⅱ';
  if (tone === 'neutral') return '↶';
  return '+';
}

function statusToneClass(status: JobStatus) {
  if (status === JobStatus.Rejected) return 'admin-case-reference-status--danger';
  if (status === JobStatus.Approved) return 'admin-case-reference-status--success';
  if (status === JobStatus.InReview) return 'admin-case-reference-status--warning';
  return 'admin-case-reference-status--info';
}

function statusGlyph(status: JobStatus) {
  if (status === JobStatus.Rejected) return '×';
  if (status === JobStatus.Approved) return '✓';
  if (status === JobStatus.InReview) return 'Ⅱ';
  return '•';
}

function AdminReferenceSkeleton() {
  return (
    <div className="page-container admin-case-reference-page" aria-label="Henter sagsoverblik">
      <div className="skeleton" style={{ width: '9rem', height: '1rem' }} />
      <div className="admin-case-reference-layout">
        <main className="admin-case-reference-main">
          <div className="skeleton" style={{ width: '70%', height: '7rem' }} />
          <div className="admin-case-reference-actions">
            {Array.from({ length: 4 }).map((_, index) => (
              <div key={index} className="skeleton" style={{ width: '8rem', height: '5rem', borderRadius: 'var(--radius)' }} />
            ))}
          </div>
          <div className="skeleton" style={{ width: '100%', height: '17rem', borderRadius: 'var(--radius)' }} />
        </main>
        <aside>
          <div className="skeleton" style={{ width: '100%', height: '34rem', borderRadius: 'var(--radius)' }} />
        </aside>
      </div>
    </div>
  );
}
