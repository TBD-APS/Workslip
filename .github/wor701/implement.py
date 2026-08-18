from pathlib import Path

ROOT = Path('.')

job_details = ROOT / 'src/FE/src/features/jobs/components/JobDetails.tsx'
launcher = ROOT / 'src/FE/src/features/jobs/components/JobConversationLauncher.tsx'
admin_view = ROOT / 'src/FE/src/features/jobs/components/AdminJobReferenceOverview.tsx'
admin_css = ROOT / 'src/FE/src/features/jobs/components/AdminJobReferenceOverview.css'

source = job_details.read_text(encoding='utf-8')

import_marker = "import { JobStatusDots } from './JobStatusDots';\n"
if "./AdminJobReferenceOverview" not in source:
    if import_marker not in source:
        raise SystemExit('JobDetails import marker not found')
    source = source.replace(import_marker, import_marker + "import { AdminJobReferenceOverview } from './AdminJobReferenceOverview';\n")

state_marker = "  const [historyOpen, setHistoryOpen] = useState(false);\n"
if "adminReferenceMode" not in source:
    if state_marker not in source:
        raise SystemExit('JobDetails state marker not found')
    source = source.replace(state_marker, state_marker + "  const [adminReferenceMode, setAdminReferenceMode] = useState(true);\n")

view_marker = "  const canSubmitForReview =\n    details.job.status === JobStatus.Draft || details.job.status === JobStatus.Rejected;\n\n"
if "<AdminJobReferenceOverview" not in source:
    if view_marker not in source:
        raise SystemExit('JobDetails view marker not found')
    block = view_marker + "  if (isAdmin && adminReferenceMode) {\n    return (\n      <>\n        <AdminJobReferenceOverview\n          details={details}\n          onBack={handleBack}\n          onEdit={() => setAdminReferenceMode(false)}\n          onOpenReport={() => onGoToReport(details.job!.id)}\n          allowSubmitForReview={canSubmitForReview}\n        />\n        <ConfirmDeleteDialog\n          open={deleteDialogOpen}\n          title=\"Slet sag\"\n          message=\"Er du sikker på, du vil slette sagen permanent? Det kan kun lade sig gøre, hvis sagen ikke har timesedler.\"\n          onConfirm={confirmDelete}\n          onClose={() => setDeleteDialogOpen(false)}\n        />\n      </>\n    );\n  }\n\n"
    source = source.replace(view_marker, block)

job_details.write_text(source, encoding='utf-8')

launcher_source = launcher.read_text(encoding='utf-8')
if "label?: string;" not in launcher_source:
    launcher_source = launcher_source.replace(
        "  className?: string;\n};",
        "  className?: string;\n  label?: string;\n};",
    )
    launcher_source = launcher_source.replace(
        "  className,\n}: JobConversationLauncherProps)",
        "  className,\n  label = 'Samtale',\n}: JobConversationLauncherProps)",
    )
    launcher_source = launcher_source.replace(
        "        {!compact && <span>Samtale</span>}",
        "        {!compact && <span>{label}</span>}",
    )
launcher.write_text(launcher_source, encoding='utf-8')

admin_view.write_text(r'''import { useMemo } from 'react';
import {
  ArrowLeft,
  CalendarDays,
  Clock3,
  Download,
  FileText,
  History,
  MapPin,
  Pencil,
  UserRound,
  UsersRound,
} from 'lucide-react';
import type { useJobDetails } from '../hooks/useJobDetails';
import { useGetApiJobsIdHistory } from '../../../api/generated/jobs/jobs';
import type { JobHistoryResponse } from '../../../api/generated/models';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { formatDateTime } from '../../../lib/formatDate';
import { formatJobStatus, formatJobType } from '../statusLabels';
import { JobConversationLauncher } from './JobConversationLauncher';
import { JobStatusDots } from './JobStatusDots';
import './AdminJobReferenceOverview.css';

type JobDetailsState = ReturnType<typeof useJobDetails>;

type Props = {
  details: JobDetailsState;
  onBack: () => void;
  onEdit: () => void;
  onOpenReport: () => void;
  allowSubmitForReview: boolean;
};

export function AdminJobReferenceOverview({
  details,
  onBack,
  onEdit,
  onOpenReport,
  allowSubmitForReview,
}: Props) {
  const job = details.job;
  const historyQuery = useGetApiJobsIdHistory(job?.id ?? '', undefined, {
    query: {
      enabled: Boolean(job?.id),
      staleTime: 15_000,
    },
    request: { skipGlobalErrorToast: true },
  });

  const orderedHistory = useMemo(() => {
    const rows = historyQuery.data ?? [];
    return [...rows].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt));
  }, [historyQuery.data]);

  if (!job) return null;

  const statusLabel = formatJobStatus(job.status);
  const reportNumber = `SAG-${(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}`;
  const title = details.form.taskDescription.trim() || 'Sag uden titel';
  const address = [
    details.form.destinationAddress,
    [details.form.destinationZipCode, details.form.destinationCity].filter(Boolean).join(' '),
  ].filter(Boolean).join(', ');
  const assigned = job.assignedUsers.map((user) => user.displayName).join(', ') || 'Ikke tildelt';
  const customer = details.form.customerSnapshot?.name?.trim() || 'Ingen kunde valgt';
  const created = orderedHistory.at(-1)?.createdAt;
  const updated = orderedHistory.at(0)?.createdAt;

  const scrollToHistory = () => {
    document.getElementById('wor701-admin-history')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  return (
    <div className="page-container wor701-admin-job-page" data-testid="wor701-admin-reference-overview">
      <div className="wor701-admin-job-shell">
        <main className="wor701-admin-job-main">
          <button type="button" className="wor701-admin-back" onClick={onBack}>
            <ArrowLeft size={17} aria-hidden="true" />
            Tilbage til sager
          </button>

          <section className="wor701-admin-hero" aria-labelledby="wor701-admin-title">
            <div className="wor701-admin-hero-copy">
              <span className="wor701-admin-kicker">{reportNumber} - {statusLabel}</span>
              <div className="wor701-admin-heading-row">
                <h1 id="wor701-admin-title">{title}</h1>
                <div className="wor701-admin-status-dots" aria-label="Sagsforløb">
                  <JobStatusDots status={job.status} />
                </div>
              </div>
              <div className="wor701-admin-meta-line">
                <span className={`wor701-admin-status-pill wor701-admin-status-pill--${String(job.status).toLowerCase()}`}>
                  {statusLabel}
                </span>
                <span aria-hidden="true">•</span>
                <span>{formatJobType(job.jobType)}</span>
                {updated && (
                  <>
                    <span aria-hidden="true">•</span>
                    <span>{formatReferenceDate(updated)}</span>
                  </>
                )}
              </div>
            </div>
          </section>

          <div className="wor701-admin-actions" aria-label="Sagshandlinger">
            <button type="button" className="wor701-admin-action" onClick={onEdit}>
              <Pencil size={21} aria-hidden="true" />
              <span>Rediger</span>
            </button>
            <JobConversationLauncher
              jobId={job.id}
              allowSubmitForReview={allowSubmitForReview}
              className="wor701-admin-action wor701-admin-conversation-action"
              label="Kommentarer"
            />
            <button type="button" className="wor701-admin-action" onClick={scrollToHistory}>
              <History size={21} aria-hidden="true" />
              <span>Historik</span>
            </button>
            <button type="button" className="wor701-admin-action" onClick={onOpenReport}>
              <Download size={21} aria-hidden="true" />
              <span>Download</span>
            </button>
          </div>

          <section className="wor701-admin-card wor701-admin-info-card" aria-labelledby="wor701-admin-info-title">
            <h2 id="wor701-admin-info-title">Sagsinformation</h2>
            <InfoRow icon={<UsersRound size={18} />} label="Kunde" value={customer} />
            <InfoRow icon={<MapPin size={18} />} label="Adresse" value={address || 'Ingen adresse angivet'} />
            <InfoRow icon={<UserRound size={18} />} label="Sagsansvarlig" value={assigned} />
            <InfoRow icon={<CalendarDays size={18} />} label="Oprettet" value={created ? formatReferenceDate(created) : '—'} />
            <InfoRow icon={<Clock3 size={18} />} label="Senest opdateret" value={updated ? formatReferenceDateTime(updated) : '—'} />
          </section>

          <section className="wor701-admin-card wor701-admin-description-card" aria-labelledby="wor701-admin-description-title">
            <h2 id="wor701-admin-description-title">Beskrivelse</h2>
            <p className="wor701-admin-description">
              {details.form.taskDescription.trim() || 'Der er endnu ikke tilføjet en beskrivelse til sagen.'}
            </p>

            <div className="wor701-admin-attachment-heading">
              <h3>Vedhæftede filer</h3>
              <span>Dokumentation og billeder håndteres fortsat i den eksisterende redigering.</span>
            </div>
            <button type="button" className="wor701-admin-file-row" onClick={onEdit}>
              <FileText size={18} aria-hidden="true" />
              <span>Åbn sagens dokumentation</span>
              <Pencil size={17} aria-hidden="true" />
            </button>
          </section>
        </main>

        <aside className="wor701-admin-sidebar" id="wor701-admin-history">
          <section className="wor701-admin-card wor701-admin-history-card" aria-labelledby="wor701-admin-history-title">
            <h2 id="wor701-admin-history-title">Sagshistorik</h2>
            {historyQuery.isLoading ? (
              <div className="wor701-admin-history-loading">Henter historik…</div>
            ) : orderedHistory.length > 0 ? (
              <div className="wor701-admin-timeline">
                {orderedHistory.slice(0, 6).map((event) => (
                  <HistoryItem key={event.id} event={event} />
                ))}
              </div>
            ) : (
              <div className="wor701-admin-history-empty">Ingen historik registreret endnu.</div>
            )}
          </section>

          {job.rejectionNote && (
            <section className="wor701-admin-card wor701-admin-leader-card" aria-labelledby="wor701-admin-leader-title">
              <h2 id="wor701-admin-leader-title">Kommentar fra leder</h2>
              <p>{job.rejectionNote}</p>
              <strong>{orderedHistory[0]?.actorName || 'Workslip'}</strong>
              {updated && <time dateTime={updated}>{formatReferenceDateTime(updated)}</time>}
            </section>
          )}
        </aside>
      </div>
    </div>
  );
}

function InfoRow({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div className="wor701-admin-info-row">
      <span className="wor701-admin-info-icon" aria-hidden="true">{icon}</span>
      <span className="wor701-admin-info-label">{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function HistoryItem({ event }: { event: JobHistoryResponse }) {
  const tone = historyTone(event);
  const title = historyTitle(event);
  return (
    <article className="wor701-admin-history-item">
      <span className={`wor701-admin-history-dot wor701-admin-history-dot--${tone}`} aria-hidden="true">
        {historyGlyph(event)}
      </span>
      <div className="wor701-admin-history-copy">
        <strong>{title}</strong>
        <time dateTime={event.createdAt}>{formatReferenceDateTime(event.createdAt)}</time>
        <span>{event.actorName || 'Workslip'}</span>
        <p>{event.summary?.trim() || historyFallback(event)}</p>
      </div>
    </article>
  );
}

function historyTitle(event: JobHistoryResponse) {
  const statusChange = event.changes.find((change) => change.propertyName.toLowerCase().includes('status'));
  if (statusChange?.after) return translateHistoryStatus(statusChange.after);
  switch (event.eventType.toLowerCase()) {
    case 'added': return 'Oprettet';
    case 'deleted': return 'Slettet';
    default: return 'Opdateret';
  }
}

function translateHistoryStatus(value: string) {
  const normalized = value.toLowerCase();
  if (normalized.includes('reject')) return 'Afvist';
  if (normalized.includes('review')) return 'Afventer';
  if (normalized.includes('approv')) return 'Gennemført';
  if (normalized.includes('reopen')) return 'Genåbnet';
  if (normalized.includes('draft')) return 'Aktiv';
  return value;
}

function historyTone(event: JobHistoryResponse) {
  const title = historyTitle(event).toLowerCase();
  if (title.includes('afvist') || title.includes('slettet')) return 'danger';
  if (title.includes('afventer')) return 'warning';
  if (title.includes('gennemført')) return 'success';
  if (title.includes('genåbnet')) return 'neutral';
  return 'info';
}

function historyGlyph(event: JobHistoryResponse) {
  const title = historyTitle(event).toLowerCase();
  if (title.includes('afvist') || title.includes('slettet')) return '×';
  if (title.includes('afventer')) return 'Ⅱ';
  if (title.includes('gennemført')) return '✓';
  if (title.includes('genåbnet')) return '↶';
  return '+';
}

function historyFallback(event: JobHistoryResponse) {
  switch (event.eventType.toLowerCase()) {
    case 'added': return 'Sagen er oprettet.';
    case 'deleted': return 'Data blev slettet fra sagen.';
    default: return 'Sagen blev opdateret.';
  }
}

function formatReferenceDate(value: string) {
  return new Intl.DateTimeFormat('da-DK', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(value));
}

function formatReferenceDateTime(value: string) {
  return formatDateTime(value) ?? formatReferenceDate(value);
}
''', encoding='utf-8')

admin_css.write_text(r'''.wor701-admin-job-page {
  max-width: 1180px;
  margin: 0 auto;
  padding-block: 0.9rem 2.5rem;
}

.wor701-admin-job-shell {
  display: grid;
  grid-template-columns: minmax(0, 1.72fr) minmax(300px, 0.92fr);
  gap: 1.2rem;
  align-items: start;
}

.wor701-admin-job-main,
.wor701-admin-sidebar {
  min-width: 0;
}

.wor701-admin-back {
  appearance: none;
  border: 0;
  background: transparent;
  color: var(--color-primary);
  display: inline-flex;
  align-items: center;
  gap: 0.42rem;
  min-height: 44px;
  padding: 0.35rem 0.2rem;
  font: inherit;
  font-size: var(--fs-sm);
  font-weight: 600;
  cursor: pointer;
}

.wor701-admin-back:focus-visible,
.wor701-admin-action:focus-visible,
.wor701-admin-file-row:focus-visible {
  outline: 2px solid var(--focus-ring);
  outline-offset: 3px;
}

.wor701-admin-hero {
  padding: 0.35rem 0 0.9rem;
}

.wor701-admin-kicker {
  display: inline-block;
  color: var(--color-primary);
  font-weight: 700;
  font-size: var(--fs-sm);
  margin-bottom: 0.5rem;
}

.wor701-admin-heading-row {
  display: flex;
  align-items: flex-start;
  gap: 1rem;
  justify-content: space-between;
}

.wor701-admin-heading-row h1 {
  margin: 0;
  color: var(--text);
  font-size: clamp(1.7rem, 3vw, 2.25rem);
  line-height: 1.08;
  letter-spacing: -0.035em;
  max-width: 660px;
}

.wor701-admin-status-dots {
  flex: 0 0 auto;
  padding-top: 0.2rem;
}

.wor701-admin-status-dots .job-status-dots {
  margin: 0;
}

.wor701-admin-meta-line {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.55rem;
  color: var(--text-secondary);
  margin-top: 0.72rem;
  font-size: var(--fs-sm);
}

.wor701-admin-status-pill {
  display: inline-flex;
  align-items: center;
  min-height: 30px;
  padding: 0.25rem 0.72rem;
  border-radius: 999px;
  border: 1px solid var(--border);
  background: var(--surface-raised);
  color: var(--text);
  font-weight: 650;
}

.wor701-admin-status-pill--rejected {
  background: var(--danger-bg);
  color: var(--danger);
  border-color: color-mix(in srgb, var(--danger) 25%, var(--border));
}

.wor701-admin-status-pill--approved {
  background: var(--success-bg);
  color: var(--success);
}

.wor701-admin-status-pill--inreview {
  background: var(--warning-bg);
  color: var(--warning);
}

.wor701-admin-actions {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 0.85rem;
  margin: 0.75rem 0 1rem;
}

.wor701-admin-action {
  appearance: none;
  min-height: 84px;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface-floating);
  color: var(--text);
  box-shadow: var(--shadow-sm-raw);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.42rem;
  font: inherit;
  font-size: var(--fs-sm);
  cursor: pointer;
  transition: transform 140ms ease, border-color 140ms ease, box-shadow 140ms ease;
}

.wor701-admin-conversation-action {
  width: 100%;
}

.wor701-admin-conversation-action .job-conversation-unread {
  position: absolute;
  transform: translate(18px, -18px);
}

.wor701-admin-card {
  background: var(--surface-floating);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow-sm-raw);
}

.wor701-admin-card h2,
.wor701-admin-card h3 {
  color: var(--text);
  margin: 0;
}

.wor701-admin-card h2 {
  font-size: 1.05rem;
}

.wor701-admin-info-card {
  padding: 1.2rem 1.35rem 0.55rem;
}

.wor701-admin-info-card h2 {
  margin-bottom: 0.75rem;
}

.wor701-admin-info-row {
  display: grid;
  grid-template-columns: 24px minmax(130px, 0.65fr) minmax(0, 1.35fr);
  align-items: center;
  gap: 0.65rem;
  min-height: 44px;
  border-top: 1px solid var(--border);
  color: var(--text-secondary);
}

.wor701-admin-info-row:first-of-type {
  border-top: 0;
}

.wor701-admin-info-icon {
  display: inline-flex;
  color: var(--text-muted);
}

.wor701-admin-info-label {
  color: var(--text-secondary);
}

.wor701-admin-info-row strong {
  color: var(--text);
  font-weight: 600;
  min-width: 0;
  overflow-wrap: anywhere;
}

.wor701-admin-description-card {
  margin-top: 1rem;
  padding: 1.25rem 1.35rem 1.35rem;
}

.wor701-admin-description {
  color: var(--text-secondary);
  line-height: 1.65;
  margin: 0.85rem 0 1.35rem;
}

.wor701-admin-attachment-heading {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 0.75rem;
  margin-bottom: 0.6rem;
}

.wor701-admin-attachment-heading h3 {
  font-size: 1rem;
}

.wor701-admin-attachment-heading span {
  color: var(--text-muted);
  font-size: var(--fs-xs);
}

.wor701-admin-file-row {
  width: 100%;
  min-height: 48px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text);
  display: grid;
  grid-template-columns: auto 1fr auto;
  gap: 0.75rem;
  align-items: center;
  padding: 0.6rem 0.75rem;
  text-align: left;
  font: inherit;
  cursor: pointer;
}

.wor701-admin-sidebar {
  display: grid;
  gap: 1rem;
  position: sticky;
  top: 1rem;
}

.wor701-admin-history-card,
.wor701-admin-leader-card {
  padding: 1.25rem 1.35rem;
}

.wor701-admin-history-card h2,
.wor701-admin-leader-card h2 {
  margin-bottom: 1rem;
}

.wor701-admin-timeline {
  position: relative;
  display: grid;
  gap: 0.15rem;
}

.wor701-admin-timeline::before {
  content: '';
  position: absolute;
  left: 17px;
  top: 18px;
  bottom: 22px;
  width: 2px;
  background: var(--border-strong);
}

.wor701-admin-history-item {
  position: relative;
  display: grid;
  grid-template-columns: 36px minmax(0, 1fr);
  gap: 0.8rem;
  padding: 0.45rem 0 1rem;
}

.wor701-admin-history-dot {
  position: relative;
  z-index: 1;
  width: 36px;
  height: 36px;
  border-radius: 50%;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: var(--surface-floating);
  font-weight: 800;
  background: var(--color-info);
  box-shadow: 0 0 0 4px var(--surface-floating);
}

.wor701-admin-history-dot--danger { background: var(--danger); }
.wor701-admin-history-dot--warning { background: var(--warning); }
.wor701-admin-history-dot--success { background: var(--success); }
.wor701-admin-history-dot--neutral { background: var(--text-muted); }

.wor701-admin-history-copy {
  display: grid;
  gap: 0.22rem;
  min-width: 0;
}

.wor701-admin-history-copy strong {
  color: var(--text);
  font-size: 0.96rem;
}

.wor701-admin-history-copy time,
.wor701-admin-history-copy span {
  color: var(--text-muted);
  font-size: var(--fs-xs);
}

.wor701-admin-history-copy p {
  color: var(--text-secondary);
  font-size: var(--fs-sm);
  line-height: 1.45;
  margin: 0.2rem 0 0;
}

.wor701-admin-history-loading,
.wor701-admin-history-empty {
  color: var(--text-muted);
  padding: 0.5rem 0;
}

.wor701-admin-leader-card p {
  color: var(--text-secondary);
  line-height: 1.55;
  margin: 0 0 1rem;
}

.wor701-admin-leader-card strong,
.wor701-admin-leader-card time {
  display: block;
}

.wor701-admin-leader-card strong {
  color: var(--text);
  margin-bottom: 0.2rem;
}

.wor701-admin-leader-card time {
  color: var(--text-muted);
  font-size: var(--fs-xs);
}

@media (hover: hover) and (pointer: fine) {
  .wor701-admin-action:hover,
  .wor701-admin-file-row:hover {
    transform: translateY(-1px);
    border-color: var(--border-strong);
    box-shadow: var(--shadow-sm);
  }
}

@media (max-width: 980px) {
  .wor701-admin-job-shell {
    grid-template-columns: 1fr;
  }

  .wor701-admin-sidebar {
    position: static;
  }
}

@media (max-width: 700px) {
  .wor701-admin-job-page {
    padding-top: 0.35rem;
  }

  .wor701-admin-heading-row {
    display: grid;
  }

  .wor701-admin-status-dots {
    order: -1;
  }

  .wor701-admin-actions {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .wor701-admin-info-row {
    grid-template-columns: 24px 1fr;
    gap: 0.35rem 0.65rem;
    padding: 0.55rem 0;
  }

  .wor701-admin-info-row strong {
    grid-column: 2;
  }

  .wor701-admin-attachment-heading {
    align-items: flex-start;
    flex-direction: column;
  }
}

@media (prefers-reduced-motion: reduce) {
  .wor701-admin-action,
  .wor701-admin-file-row {
    transition: none;
  }
}
''', encoding='utf-8')

expected = {
    'src/FE/src/features/jobs/components/JobDetails.tsx',
    'src/FE/src/features/jobs/components/JobConversationLauncher.tsx',
    'src/FE/src/features/jobs/components/AdminJobReferenceOverview.tsx',
    'src/FE/src/features/jobs/components/AdminJobReferenceOverview.css',
}

print('WOR-701 implementation prepared:')
for path in sorted(expected):
    print(' -', path)
