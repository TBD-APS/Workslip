import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { AlertCircle, ArrowLeft, CheckCircle2, Eye, FileCheck2, History, Link2, Loader2, Pencil, Timer, User, X } from 'lucide-react';
import { toast } from 'sonner';
import { useGetApiJobsId } from '../../../api/generated/jobs/jobs';
import type {
  InstallationTypeResponse,
  JobLinkInfoResponse,
  JobReportSummaryViewModel,
  WorksheetResponse,
} from '../../../api/generated/models';
import { getResponseData } from '../utils';
import { createJobReportPdfPreview, type JobReportPdfPreview } from '../utils/downloadJobReportPdf';

const DATE_FORMATTER = new Intl.DateTimeFormat('da-DK', { day: '2-digit', month: '2-digit', year: 'numeric' });
const NUMBER_FORMATTER = new Intl.NumberFormat('da-DK', { maximumFractionDigits: 2 });

type DetailPair = { label: string; value: string | null | undefined };

type SelectedControlPoint = {
  id: string;
  installationType: string;
  category: string;
  name: string;
  description: string | null;
};

type IrrelevantCategory = {
  id: string;
  installationType: string;
  category: string;
};

export const CompletedJobReport = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [isOpeningPdf, setIsOpeningPdf] = useState(false);
  const [pdfPreview, setPdfPreview] = useState<JobReportPdfPreview | null>(null);
  const query = useGetApiJobsId(id ?? '', { query: { enabled: Boolean(id) } });
  const job = getResponseData<JobReportSummaryViewModel>(query.data);

  const selectedControlPoints = useMemo(() => getSelectedControlPoints(job?.work.installationTypes ?? []), [job?.work.installationTypes]);
  const irrelevantCategories = useMemo(() => getIrrelevantCategories(job?.work.installationTypes ?? []), [job?.work.installationTypes]);
  const sortedWorksheets = useMemo(
    () => [...(job?.worksheets ?? [])].sort((left, right) => right.workDate.localeCompare(left.workDate)),
    [job?.worksheets],
  );

  useEffect(() => {
    return () => {
      if (pdfPreview) {
        window.URL.revokeObjectURL(pdfPreview.url);
      }
    };
  }, [pdfPreview]);

  const handleOpenPdf = async () => {
    if (!job) return;
    setIsOpeningPdf(true);

    try {
      setPdfPreview(await createJobReportPdfPreview(job));
    } catch {
      toast.error('Kunne ikke åbne PDF for sagen');
    } finally {
      setIsOpeningPdf(false);
    }
  };

  if (query.isLoading) {
    return (
      <div className="page-container report-overview-page">
        <div className="detail-loading">
          <Loader2 size={24} className="spin" />
          <p>Henter sagsrapport...</p>
        </div>
      </div>
    );
  }

  if (query.isError || !job) {
    return (
      <div className="page-container report-overview-page">
        <div className="error-state">
          <AlertCircle size={32} />
          <p>Kunne ikke hente sagsrapporten.</p>
          <button className="btn btn-primary" onClick={() => query.refetch()}>
            Prøv igen
          </button>
        </div>
      </div>
    );
  }

  const summaryPairs = compactPairs([
    { label: 'Sagsnummer', value: formatReportNumber(job) },
    { label: 'Status', value: formatStatus(job.status) },
    { label: 'Rapportdato', value: formatDate(job.observations.reportDate) },
    { label: 'Opgavetype', value: formatWorkKind(job) },
    { label: 'Anlægstyper', value: formatInstallationTypeNames(job.work.installationTypes) },
    { label: 'Afslutning', value: formatClosureFlags(job) },
  ]);
  const customerPairs = compactPairs([
    { label: 'Kunde', value: job.customer.name },
    { label: 'Adresse', value: job.customer.address },
    { label: 'Kontaktperson', value: job.customer.contactPerson },
    { label: 'Telefon', value: job.customer.phone },
    { label: 'Email', value: job.customer.email },
  ]);
  const observationPairs = compactPairs([
    { label: 'Opgave', value: job.observations.taskDescription },
    { label: 'Kundeinfo', value: job.observations.customerObservations },
    { label: 'Teknisk', value: job.observations.technicalObservations },
    { label: 'Bemærkninger', value: job.work.remarks },
  ]);

  return (
    <div className="page-container report-overview-page">
      <div className="detail-header report-overview-header">
        <button className="btn-icon" type="button" onClick={() => navigate('/app/completed')} aria-label="Tilbage til afsluttede sager">
          <ArrowLeft size={22} />
        </button>
        <div>
          <span className="job-number">{formatReportNumber(job)}</span>
          <h2 className="detail-title">Komplet sagsoverblik</h2>
        </div>
        <div className="report-overview-actions" aria-label="Rapport handlinger">
          <button className="btn btn-secondary report-overview-icon-action" type="button" disabled aria-label="Rediger sag" title="Rediger sag kommer senere">
            <Pencil size={16} />
          </button>
          <button className="btn btn-secondary report-overview-icon-action" type="button" disabled aria-label="Versioner" title="Versioner kommer senere">
            <History size={16} />
          </button>
          <button className="btn btn-secondary pdf-download-button report-overview-pdf" type="button" onClick={handleOpenPdf} disabled={isOpeningPdf}>
            {isOpeningPdf ? <Loader2 size={16} className="spin" /> : <Eye size={16} />}
            <span>{isOpeningPdf ? 'Åbner...' : 'Vis PDF'}</span>
          </button>
        </div>
      </div>

      <section className="detail-section">
        <div className="section-header-row attestation-compact-header">
          <User size={18} />
          <h3>Kunde og bemanding</h3>
        </div>
        <DetailGrid items={customerPairs} />
        <AssignedUsers users={job.assignedUsers} />
      </section>

      <section className="detail-section report-overview-hero">
        <div className="section-header-row attestation-compact-header">
          <FileCheck2 size={18} />
          <h3>Sag</h3>
        </div>
        <span className={`status-badge status-${job.status.toString().toLowerCase()}`}>{formatStatus(job.status)}</span>
        <DetailGrid items={summaryPairs} />
      </section>

      <section className="detail-section">
        <div className="section-header-row attestation-compact-header">
          <Link2 size={18} />
          <h3>Tilknyttede sager</h3>
        </div>
        <LinkedJobs links={job.links} onOpen={(linkedJobId) => navigate(`/app/completed/${linkedJobId}`)} />
      </section>

      <section className="detail-section attestation-timesheet-section">
        <div className="section-header-row attestation-compact-header">
          <Timer size={18} />
          <h3>Timesedler</h3>
        </div>
        <Worksheets worksheets={sortedWorksheets} />
        <div className="attestation-timesheet-totals" aria-label="Timeseddel totaler">
          <span>{formatNumber(job.totalHours)} {formatUnit(parseNullableNumber(job.totalHours), 'time', 'timer')}</span>
          {parseNullableNumber(job.totalOutlay) > 0 && (
            <span>{formatNumber(job.totalOutlay)} {formatUnit(parseNullableNumber(job.totalOutlay), 'udlæg', 'udlæg')}</span>
          )}
        </div>
      </section>

      {observationPairs.length > 0 && (
        <section className="detail-section attestation-summary-section">
          <div className="section-header-row attestation-compact-header">
            <FileCheck2 size={18} />
            <h3>Observationer og noter</h3>
          </div>
          <div className="attestation-observations-list">
            {observationPairs.map((item) => (
              <div key={item.label} className="attestation-data-pair observation">
                <dt>{item.label}</dt>
                <dd>{item.value}</dd>
              </div>
            ))}
          </div>
        </section>
      )}

      <section className="detail-section attestation-control-section compact">
        <div className="section-header-row attestation-compact-header">
          <CheckCircle2 size={18} />
          <h3>Kontrolpunkter</h3>
        </div>
        <ControlPointOverview selectedControlPoints={selectedControlPoints} irrelevantCategories={irrelevantCategories} />
      </section>

      {pdfPreview && <PdfPreviewDialog preview={pdfPreview} onClose={() => setPdfPreview(null)} />}
    </div>
  );
};

function PdfPreviewDialog({ preview, onClose }: { preview: JobReportPdfPreview; onClose: () => void }) {
  return (
    <div className="pdf-preview-overlay" role="dialog" aria-modal="true" aria-label="PDF rapport">
      <div className="pdf-preview-panel">
        <div className="pdf-preview-header">
          <div>
            <span className="job-number">PDF rapport</span>
            <h3>{preview.fileName}</h3>
            <p>Brug PDF-viserens egen download-knap, hvis rapporten skal gemmes.</p>
          </div>
          <button className="btn-icon" type="button" onClick={onClose} aria-label="Luk PDF">
            <X size={22} />
          </button>
        </div>
        <iframe className="pdf-preview-frame" src={`${preview.url}#toolbar=1&navpanes=0`} title={preview.fileName} />
      </div>
    </div>
  );
}

function DetailGrid({ items }: { items: DetailPair[] }) {
  if (items.length === 0) {
    return <p className="empty-state-text">Ingen oplysninger registreret.</p>;
  }

  return (
    <dl className="attestation-data-list report-overview-data-list">
      {items.map((item) => (
        <div key={item.label} className="attestation-data-pair">
          <dt>{item.label}</dt>
          <dd>{item.value}</dd>
        </div>
      ))}
    </dl>
  );
}

function AssignedUsers({ users }: { users: JobReportSummaryViewModel['assignedUsers'] }) {
  if (users.length === 0) {
    return <p className="empty-state-text report-overview-block-gap">Ingen montører tildelt.</p>;
  }

  return (
    <div className="report-overview-chip-list report-overview-block-gap">
      {users.map((user) => (
        <span key={user.id} className="assigned-user report-overview-chip">
          <User size={12} />
          <span>{user.displayName}</span>
        </span>
      ))}
    </div>
  );
}

function LinkedJobs({ links, onOpen }: { links: JobLinkInfoResponse[]; onOpen: (linkedJobId: string) => void }) {
  if (links.length === 0) {
    return <p className="empty-state-text">Ingen tilknyttede sager.</p>;
  }

  return (
    <div className="report-overview-link-list">
      {links.map((link) => (
        <button key={link.id} type="button" className="report-overview-link-card" onClick={() => onOpen(link.linkedReportId)}>
          <span className="job-number">SAG-{link.linkedReportNumber || link.linkedReportId.slice(0, 4).toUpperCase()}</span>
          <span className="report-overview-link-title">{link.linkedCustomerName || 'Ukendt kunde'}</span>
          <span className={`status-badge status-${link.linkedStatus.toLowerCase()}`}>{formatStatus(link.linkedStatus)}</span>
        </button>
      ))}
    </div>
  );
}

function Worksheets({ worksheets }: { worksheets: WorksheetResponse[] }) {
  if (worksheets.length === 0) {
    return <p className="empty-state-text">Ingen timesedler registreret.</p>;
  }

  return (
    <ul className="attestation-timesheet-list report-overview-timesheet-list">
      {worksheets.map((worksheet) => {
        const hours = parseNullableNumber(worksheet.hoursWorked);
        return (
          <li key={worksheet.id}>
            <div className="attestation-timesheet-main">
              <span className="attestation-timesheet-date">{formatDate(worksheet.workDate)}</span>
              <span className="attestation-timesheet-user">{worksheet.userDisplayName}</span>
            </div>
            <div className="attestation-timesheet-hours">
              <span className="attestation-timesheet-hours-value">{formatNumber(hours)}</span>
              <span className="attestation-timesheet-hours-unit">{formatUnit(hours, 'time', 'timer')}</span>
            </div>
            {worksheet.sleptOnJob && <span className="attestation-timesheet-badge">Udlæg</span>}
          </li>
        );
      })}
    </ul>
  );
}

function ControlPointOverview({
  selectedControlPoints,
  irrelevantCategories,
}: {
  selectedControlPoints: SelectedControlPoint[];
  irrelevantCategories: IrrelevantCategory[];
}) {
  if (selectedControlPoints.length === 0 && irrelevantCategories.length === 0) {
    return <p className="empty-state-text">Ingen kontrolpunkter markeret.</p>;
  }

  return (
    <>
      {selectedControlPoints.length > 0 && (
        <ul className="attestation-control-list compact">
          {selectedControlPoints.map((controlPoint) => (
            <li key={controlPoint.id}>
              <span className="attestation-control-accent" aria-hidden="true" />
              <span>{controlPoint.name}</span>
              <small>{controlPoint.installationType} · {capitalize(controlPoint.category)}</small>
              {controlPoint.description && <small>{controlPoint.description}</small>}
            </li>
          ))}
        </ul>
      )}

      {irrelevantCategories.length > 0 && (
        <div className="attestation-irrelevant-block">
          <span className="attestation-irrelevant-label">Markeret irrelevant</span>
          <ul className="attestation-control-list compact">
            {irrelevantCategories.map((item) => (
              <li key={item.id} className="attestation-control-list-item-muted">
                <span className="attestation-control-accent muted" aria-hidden="true" />
                <span>{item.installationType} · {capitalize(item.category)}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </>
  );
}

function getSelectedControlPoints(installationTypes: InstallationTypeResponse[]): SelectedControlPoint[] {
  return installationTypes.flatMap((installationType) =>
    installationType.categories.flatMap((category) =>
      category.controlPoints
        .filter((controlPoint) => controlPoint.isChecked)
        .map((controlPoint) => ({
          id: controlPoint.id,
          installationType: installationType.name,
          category: category.name,
          name: controlPoint.name,
          description: controlPoint.description,
        })),
    ),
  );
}

function getIrrelevantCategories(installationTypes: InstallationTypeResponse[]): IrrelevantCategory[] {
  return installationTypes.flatMap((installationType) =>
    installationType.categories
      .filter((category) => category.isIrrelevant)
      .map((category) => ({
        id: `${installationType.id}-${category.id}`,
        installationType: installationType.name,
        category: category.name,
      })),
  );
}

function compactPairs(items: DetailPair[]) {
  return items.filter((item): item is { label: string; value: string } => hasText(item.value));
}

function hasText(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function formatReportNumber(job: Pick<JobReportSummaryViewModel, 'id' | 'reportNumber'>) {
  return `SAG-${(job.reportNumber || job.id.slice(0, 4)).toUpperCase()}`;
}

function formatStatus(status: string) {
  const labels: Record<string, string> = {
    Draft: 'Kladde',
    Submitted: 'Indsendt',
    InReview: 'Under review',
    Approved: 'Godkendt',
    Rejected: 'Afvist',
    Archived: 'Arkiveret',
  };

  return labels[status] ?? status;
}

function formatWorkKind(job: JobReportSummaryViewModel) {
  const workKind = job.work.workKind;
  if (!workKind) return null;
  if (workKind.customWorkKind) return `${workKind.label}: ${workKind.customWorkKind}`;
  return workKind.label;
}

function formatInstallationTypeNames(installationTypes: InstallationTypeResponse[]) {
  const names = installationTypes.map((installationType) => installationType.name).filter(hasText);
  return names.length > 0 ? names.join(', ') : null;
}

function formatClosureFlags(job: JobReportSummaryViewModel) {
  const labels = job.work.closureFlags.map((flag) => flag.label).filter(hasText);
  return labels.length > 0 ? labels.join(', ') : null;
}

function formatDate(value: string | null | undefined) {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return DATE_FORMATTER.format(date);
}

function parseNullableNumber(value: number | string | null) {
  if (value === null) return 0;
  const parsed = typeof value === 'number' ? value : Number(value.replace(',', '.'));
  return Number.isFinite(parsed) ? parsed : 0;
}

function formatNumber(value: number | string | null) {
  return NUMBER_FORMATTER.format(parseNullableNumber(value));
}

function formatUnit(value: number, singular: string, plural: string) {
  return Math.abs(value) === 1 ? singular : plural;
}

function capitalize(value: string) {
  if (value.length === 0) return value;
  return `${value[0].toLocaleUpperCase('da-DK')}${value.slice(1)}`;
}
