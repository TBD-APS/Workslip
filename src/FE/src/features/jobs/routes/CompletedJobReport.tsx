import { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, CheckCircle2, ChevronRight, Download, Eye, FileCheck2, History, Link2, Loader2, Pencil, Save, ShieldCheck, Timer, User, X } from 'lucide-react';
import { notify } from '../../../lib/toast';
import { ErrorState } from '../../../components/ErrorState';
import { StatusBanner } from '../../../components/StatusBanner';
import { getGetApiJobsIdQueryKey, getGetApiJobsQueryKey, usePostApiJobsIdStatus } from '../../../api/generated/jobs/jobs';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { useIsAdmin } from '../../../providers/permissions/usePermissions';
import { useAuth } from '../../../providers/useAuth';
import { validateControlPoints } from '../components/steps/controlPointsValidation';
import { CollapsibleSection } from '../../../components/forms/CollapsibleSection';
import { NavigationGuard } from '../../../components/forms/NavigationGuard';
import { useJobDetailsState } from '../hooks/useJobDetails';
import { markJobAsSeen } from '../utils/markJobSeen';
import { useScrollRestore } from '../../../hooks/useScrollRestore';

import { formatJobStatus } from '../statusLabels';
import { createJobReportPdfPreview, downloadJobReportPdf } from '../utils/downloadJobReportPdf';
import { JobHistoryDrawer } from '../components/JobHistoryDrawer';
import { useMediaQuery } from '../../../hooks/useMediaQuery';
import { compactPairs, formatNumber, formatUnit, parseNullableNumber } from '../../../lib/formatUtils';
import { ConfirmActionDialog } from '../components/ConfirmActionDialog';
import { CompletedJobEditForm } from '../components/CompletedJobEditForm';
import { DetailGrid } from '../components/DetailGrid';
import { AssignedUsers } from '../components/AssignedUsers';
import { LinkedJobs } from '../components/LinkedJobs';
import { WorksheetDetailList } from '../components/WorksheetDetailList';
import { ControlPointOverview, getSelectedControlPoints, getIrrelevantCategories } from '../components/ControlPointOverview';
import { formatReportNumber, formatWorkKind, formatInstallationTypeNames, formatClosureFlags } from '../utils/completedJobFormatters';

function scrollToTop() {
  document.querySelector<HTMLElement>('.app-shell')?.scrollTo(0, 0);
}

export const CompletedJobReport = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const location = useLocation();
  const from = (location.state as { from?: string } | undefined)?.from ?? '/app';
  const readOnly = Boolean((location.state as { readOnly?: boolean } | undefined)?.readOnly);
  const details = useJobDetailsState(id, { autoSave: false });

  useScrollRestore(`completed:${id}`);
  const isAdmin = useIsAdmin();
  const { user } = useAuth();
  const isDesktop = useMediaQuery('(min-width: 768px)');
  const statusMutation = usePostApiJobsIdStatus();
  const [isDownloadingPdf, setIsDownloadingPdf] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [confirmAction, setConfirmAction] = useState<'approve' | 'reject' | 'undo-reject' | null>(null);
  const [undoRejectionCompleted, setUndoRejectionCompleted] = useState(false);
  const [isLoadingPreview, setIsLoadingPreview] = useState(false);
  const [worksheetOpen, setWorksheetOpen] = useState(true);
  const previewUrlRef = useRef<string | null>(null);

  const job = details.job;
  const isDiverseInReview = job?.jobType === 'Diverse' && job?.status === JobStatus.InReview;

  useEffect(() => {
    if (!id) return;
    markJobAsSeen(id, queryClient);
    if (job?.status === JobStatus.Rejected) {
      markJobAsSeen(id, queryClient, 'RejectedAssignment');
    }
  }, [id, job?.status]);

  const selectedControlPoints = useMemo(() => getSelectedControlPoints(job?.work.installationTypes ?? []), [job?.work.installationTypes]);
  const irrelevantCategories = useMemo(() => getIrrelevantCategories(job?.work.installationTypes ?? []), [job?.work.installationTypes]);
  const visibleWorksheets = useMemo(() => {
    if (!isAdmin) {
      return (job?.worksheets ?? []).filter((ws) => ws.userId === user?.id);
    }
    return job?.worksheets ?? [];
  }, [job?.worksheets, isAdmin, user?.id]);

  const initialLoadDone = useRef(false);
  const editScrollDone = useRef(false);

  useEffect(() => {
    if (!job || initialLoadDone.current) return;
    initialLoadDone.current = true;
    scrollToTop();
    requestAnimationFrame(() => scrollToTop());
  }, [job]);

  useEffect(() => {
    if (!isEditing || editScrollDone.current) return;
    editScrollDone.current = true;
    scrollToTop();
    requestAnimationFrame(() => scrollToTop());
  }, [isEditing]);

  useEffect(() => {
    if (!isEditing) editScrollDone.current = false;
  }, [isEditing]);

  const handleDownloadPdf = async () => {
    if (!job) return;
    setIsDownloadingPdf(true);

    try {
      await downloadJobReportPdf(job);
    } catch {
      notify.error(`Kunne ikke hente PDF for sagen ${details.form.reportNumber}`);
    } finally {
      setIsDownloadingPdf(false);
    }
  };

  const handlePreviewPdf = async () => {
    if (!job) return;
    setIsLoadingPreview(true);

    try {
      const { url } = await createJobReportPdfPreview(job);
      if (previewUrlRef.current) {
        window.URL.revokeObjectURL(previewUrlRef.current);
      }
      previewUrlRef.current = url;
      window.open(url, '_blank');
      setTimeout(() => {
        if (previewUrlRef.current === url) {
          window.URL.revokeObjectURL(url);
          previewUrlRef.current = null;
        }
      }, 60000);
    } catch {
      notify.error(`Kunne ikke hente PDF for sagen ${details.form.reportNumber}`);
    } finally {
      setIsLoadingPreview(false);
    }
  };

  const handleStartEdit = () => {
    details.discardChanges();
    setIsEditing(true);
    scrollToTop();
  };

  const handleCancelEdit = () => {
    details.discardChanges();
    setIsEditing(false);
    scrollToTop();
  };

  const handleSaveEdit = async () => {
    const cpValidation = validateControlPoints(details.form, details.referenceData!);
    if (!cpValidation.valid) {
      notify.error(cpValidation.error ?? 'Udfyld venligst alle påkrævede kontrolpunkter');
      return;
    }

    const saved = await details.saveAllChanges();
    if (!saved) return;

    setIsEditing(false);
    scrollToTop();
    notify.success(`Sagen ${details.form.reportNumber} er opdateret`);
  };

  const handleApprove = () => {
    setConfirmAction('approve');
  };

  const handleReject = () => {
    setConfirmAction('reject');
  };

  const handleUndoRejection = () => {
    setConfirmAction('undo-reject');
  };

  const executeConfirmAction = async (rejectionNote?: string) => {
    if (!job || !confirmAction) return;

    let targetStatus: JobStatus;
    let note: string | null = null;

    if (confirmAction === 'undo-reject') {
      targetStatus = JobStatus.InReview;
    } else {
      targetStatus = confirmAction === 'approve' ? JobStatus.Approved : JobStatus.Rejected;
      note = rejectionNote ?? null;
    }

    try {
      const updatedJob = await statusMutation.mutateAsync({ id: job.id, data: { status: targetStatus, rejectionNote: note } });
      queryClient.setQueryData(getGetApiJobsIdQueryKey(job.id), updatedJob);
      await queryClient.invalidateQueries({ queryKey: getGetApiJobsQueryKey() });
      const message = confirmAction === 'undo-reject'
        ? `Sagen ${details.form.reportNumber} er sendt til gennemgang igen`
        : confirmAction === 'approve'
          ? `${details.form.reportNumber} er godkendt`
          : `${details.form.reportNumber} er afvist`;
      setConfirmAction(null);

      if (confirmAction === 'undo-reject') {
        setUndoRejectionCompleted(true);
        return;
      }

      notify.success(message);
      navigate(from);
    } catch {
      const message = confirmAction === 'undo-reject'
        ? `Kunne ikke fortryde afvisningen. Prøv igen.`
        : confirmAction === 'approve'
          ? `Kunne ikke godkende ${details.form.reportNumber}. Prøv igen.`
          : `Kunne ikke afvise ${details.form.reportNumber}. Prøv igen.`;
      notify.error(message);
      setConfirmAction(null);
    }
  };

  if (details.isLoading) {
    return (
      <div className="page-container report-overview-page">
        <div className="detail-header report-overview-header">
          <div className="skeleton skeleton-icon" />
          <div>
            <div className="skeleton" style={{ width: '10rem', height: '0.85rem', marginBottom: '0.35rem' }} />
            <div className="skeleton" style={{ width: '8rem', height: '1.25rem' }} />
          </div>
        </div>
        <div className="report-overview-toolbar" aria-label="Rapport handlinger">
          <div className="report-overview-actions report-overview-actions--left">
            <div className="skeleton" style={{ width: '2rem', height: '2rem', borderRadius: 'var(--radius-sm)' }} />
            <div className="skeleton" style={{ width: '2rem', height: '2rem', borderRadius: 'var(--radius-sm)' }} />
          </div>
        </div>
        <div className="report-overview-grid">
          <div className="report-overview-colpair">
            <section className="detail-section">
              <div className="skeleton" style={{ width: '6rem', height: '1rem', marginBottom: '0.75rem' }} />
              <div className="skeleton" style={{ width: '100%', height: '6rem' }} />
            </section>
            <section className="detail-section">
              <div className="skeleton" style={{ width: '6rem', height: '1rem', marginBottom: '0.75rem' }} />
              <div className="skeleton" style={{ width: '100%', height: '6rem' }} />
            </section>
          </div>
          <section className="detail-section">
            <div className="skeleton" style={{ width: '7rem', height: '1rem', marginBottom: '0.75rem' }} />
            <div className="skeleton" style={{ width: '60%', height: '3rem' }} />
          </section>
        </div>
      </div>
    );
  }

  if (details.isError || !job) {
    return (
      <div className="page-container report-overview-page">
        <ErrorState message="Kunne ikke hente sagsrapporten." onRetry={() => details.refetch()} />
      </div>
    );
  }

  const summaryPairs = compactPairs([
    { label: 'Sagsnummer', value: formatReportNumber(job) },
    { label: 'Status', value: formatJobStatus(job.status)},
    { label: 'Anlægstyper', value: formatInstallationTypeNames(job.work.installationTypes) },
    { label: 'Opgavetype', value: formatWorkKind(job) },
    { label: 'Destination', value: job.destinationAddress },
    { label: 'Afslutning', value: formatClosureFlags(job) },
    { label: 'Afvisningsgrund', value: job.status === JobStatus.Rejected ? job.rejectionNote : undefined },
  ]);
  const customerPairs = compactPairs([
    { label: 'Kunde', value: job.customerSnapshot.name },
    { label: 'Adresse', value: job.customerSnapshot.address },
    { label: 'Kontaktperson', value: job.customerSnapshot.contactPerson },
    { label: 'Telefon', value: job.customerSnapshot.phone },
    { label: 'Email', value: job.customerSnapshot.email },
  ]);
  const observationPairs = compactPairs([
    { label: 'Opgave', value: job.observations.taskDescription },
    { label: 'Kundeinfo', value: job.observations.customerObservations },
    { label: 'Teknisk', value: job.observations.technicalObservations },
    { label: 'Bemærkninger', value: job.work.remarks },
  ]);

  return (
    <div className="page-container report-overview-page">
      <NavigationGuard when={isEditing && details.hasUnsavedChanges} onSave={() => details.saveAllChanges()} />
      <div className="detail-header report-overview-header">
        <button className="btn-icon" type="button" onClick={() => navigate(-1)} aria-label="Tilbage til afsluttede sager">
          <ArrowLeft size={22} />
        </button>
        <div>
          <span className="job-number">{formatReportNumber(job)} - {formatJobStatus(job.status)}</span>
          <h2 className="detail-title">Sagsoverblik</h2>
        </div>
      </div>
      <div className="report-overview-toolbar" aria-label="Rapport handlinger">
        <div className="report-overview-actions report-overview-actions--left">
          {isEditing ? (
            <>
              <button className="btn btn-secondary report-overview-icon-action edit-form-cancel-btn" type="button" onClick={handleCancelEdit} aria-label="Annuller redigering" disabled={details.saveStatus === 'saving'}>
                <X size={16} />
              </button>
              <button className="btn btn-primary edit-form-header-save-btn" type="button" onClick={handleSaveEdit} aria-label="Gem ændringer" disabled={details.saveStatus === 'saving'}>
                {details.saveStatus === 'saving' ? <Loader2 size={18} className="spin" /> : <Save size={18} />}
                <span>{details.saveStatus === 'saving' ? 'Gemmer...' : 'Gem ændringer'}</span>
              </button>
            </>
          ) : (
            isAdmin && !readOnly && (
              <button className="btn btn-secondary report-overview-icon-action" type="button" onClick={handleStartEdit} aria-label="Rediger sag">
                <Pencil size={16} />
              </button>
            )
          )}
          <button
            className={`btn btn-secondary report-overview-icon-action`}
            type="button"
            onClick={() => setHistoryOpen(true)}
            disabled={isEditing}
            aria-label="Historik"
            title="Vis sagshistorik"
          >
            <History size={16} />
          </button>
          {isDesktop && (
            <button
              className="btn btn-secondary report-overview-icon-action"
              type="button"
              onClick={() => void handlePreviewPdf()}
              disabled={isLoadingPreview || isEditing}
              aria-label="Forhåndsvis PDF"
              title="Forhåndsvis PDF"
            >
              {isLoadingPreview ? <Loader2 size={16} className="spin" /> : <Eye size={16} />}
            </button>
          )}
          <button
            className="btn btn-secondary report-overview-icon-action"
            type="button"
            onClick={() => void handleDownloadPdf()}
            disabled={isDownloadingPdf || isEditing}
            aria-label="Download PDF"
            title="Download PDF"
          >
            {isDownloadingPdf ? <Loader2 size={16} className="spin" /> : <Download size={16} />}
          </button>
        </div>
        {isDesktop && job.status === JobStatus.InReview && isAdmin && !readOnly && !isEditing && (
          <div className="report-overview-actions report-overview-actions--right">
            <button className="btn btn-secondary" type="button" onClick={handleReject} disabled={statusMutation.isPending}>
              <X size={16} />
              <span>Afvis</span>
            </button>
            <button className="btn btn-primary" type="button" onClick={handleApprove} disabled={statusMutation.isPending}>
              {statusMutation.isPending ? <Loader2 size={16} className="spin" /> : <CheckCircle2 size={16} />}
              <span>{statusMutation.isPending ? 'Godkender...' : 'Godkend'}</span>
            </button>
          </div>
        )}
        {isDesktop && job.status === JobStatus.Rejected && isAdmin && !readOnly && !isEditing && (
          <div className="report-overview-actions report-overview-actions--right">
            <button className="btn btn-secondary" type="button" onClick={handleUndoRejection} disabled={statusMutation.isPending}>
              <X size={16} />
              <span>Fortryd afvisning</span>
            </button>
          </div>
        )}
      </div>

      {isAdmin && job.status === JobStatus.InReview && !readOnly && (
        <StatusBanner variant="info" title="Klar til review">
          <p>Sagen er sendt til gennemgang og mangler din godkendelse.</p>
        </StatusBanner>
      )}

      {isEditing ? (
        <CompletedJobEditForm details={details} onCancel={handleCancelEdit} onSave={handleSaveEdit} />
      ) : (
        <>
          <div className="report-overview-grid">
            <div className="report-overview-colpair">
              <section className="detail-section report-overview-hero">
                <div className="section-header-row attestation-compact-header">
                  <FileCheck2 size={18} />
                  <h3>Sag</h3>
                </div>
                <DetailGrid items={summaryPairs} />
              </section>
              {!isDiverseInReview && (
                <section className="detail-section">
                  <div className="section-header-row attestation-compact-header">
                    <User size={18} />
                    <h3>Kunde</h3>
                  </div>
                  <DetailGrid items={customerPairs} />
                </section>
              )}
            </div>

            <section className="detail-section">
              <button
                className="section-header-row attestation-compact-header btn-reset"
                type="button"
                onClick={() => setWorksheetOpen(o => !o)}
                aria-expanded={worksheetOpen}
              >
                <Timer size={18} />
                <h3>Timesedler ({visibleWorksheets.length})</h3>
                <ChevronRight
                  size={18}
                  className="chevron-icon"
                  style={{ transform: worksheetOpen ? 'rotate(90deg)' : 'none' }}
                />
              </button>

              {worksheetOpen && (
                <div className="worksheet-list-section">
                  <WorksheetDetailList worksheets={visibleWorksheets} className="report-overview-timesheet-list" />
                  <div className="worksheet-list-totals" aria-label="Timeseddel totaler">
                    {parseNullableNumber(job.totalOutlay) > 0 && (
                      <span><strong>{formatNumber(job.totalOutlay)}</strong> {formatUnit(parseNullableNumber(job.totalOutlay), 'udlæg', 'udlæg')}</span>
                    )}
                    <span><strong>{formatNumber(job.totalHours)}</strong> {formatUnit(parseNullableNumber(job.totalHours), 'time', 'timer')}</span>
                  </div>
                </div>
              )}
            </section>

            <section className="detail-section">
              <div className="section-header-row">
                <User size={18} />
                <h3>Medarbejdere</h3>
              </div>
              <AssignedUsers users={job.assignedUsers} />
            </section>

            {observationPairs.length > 0 ? (
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
            ) : (
              <section className="detail-section">
                <div className="section-header-row attestation-compact-header">
                  <FileCheck2 size={18} />
                  <h3>Observationer og noter</h3>
                </div>
                <p className="empty-state-text">Ingen observationer registreret.</p>
              </section>
            )}

            {!isDiverseInReview && (
              <section className="detail-section">
                <div className="section-header-row attestation-compact-header">
                  <Link2 size={18} />
                  <h3>Tilknyttede sager</h3>
                </div>
                <LinkedJobs links={job.links} onOpen={(linkedJobId) => navigate(`/app/completed/${linkedJobId}`, { state: { from } })} />
              </section>
            )}

          </div>

          {!isDiverseInReview && (
            <CollapsibleSection
              icon={<CheckCircle2 size={18} />}
              title="Kontrolpunkter"
              className="kontrolpunkter-section"
            >
              <div className="attestation-control-section compact">
                <ControlPointOverview selectedControlPoints={selectedControlPoints} irrelevantCategories={irrelevantCategories} />
              </div>
            </CollapsibleSection>
          )}

          {!isDesktop && job.status === JobStatus.InReview && isAdmin && !readOnly && !isEditing && (
            <section className="detail-section">
              <div className="section-header-row">
                <ShieldCheck size={18} />
                <h3>Godkendelse</h3>
              </div>
              <div className="edit-form-bottom-actions">
                <button className="btn btn-secondary edit-form-bottom-btn" type="button" onClick={handleReject} disabled={statusMutation.isPending}>
                  <X size={18} />
                  Afvis
                </button>
                <button className="btn btn-primary edit-form-bottom-btn" type="button" onClick={handleApprove} disabled={statusMutation.isPending}>
                  {statusMutation.isPending ? <Loader2 size={18} className="spin" /> : <CheckCircle2 size={18} />}
                  {statusMutation.isPending ? 'Godkender...' : 'Godkend'}
                </button>
              </div>
            </section>
          )}

          {!isDesktop && job.status === JobStatus.Rejected && isAdmin && !readOnly && !isEditing && (
            <section className="detail-section">
              <div className="section-header-row">
                <ShieldCheck size={18} />
                <h3>Godkendelse</h3>
              </div>
              <div className="edit-form-bottom-actions">
                <button className="btn btn-secondary edit-form-bottom-btn" type="button" onClick={handleUndoRejection} disabled={statusMutation.isPending}>
                  <X size={18} />
                  Fortryd afvisning
                </button>
              </div>
            </section>
          )}
        </>
      )}

      <JobHistoryDrawer
        jobId={job.id} 
        isOpen={historyOpen} 
        onClose={() => setHistoryOpen(false)} 
      />

      {confirmAction && (
        <ConfirmActionDialog
          action={confirmAction}
          reportNumber={details.form.reportNumber}
          isPending={statusMutation.isPending}
          onConfirm={(note) => void executeConfirmAction(note)}
          onClose={() => setConfirmAction(null)}
        />
      )}

      {undoRejectionCompleted && (
        <UndoRejectionSuccessDialog
          reportNumber={formatReportNumber(job)}
          onGoToJobList={() => navigate('/app', { replace: true })}
          onGoToJob={() => setUndoRejectionCompleted(false)}
        />
      )}
    </div>
  );
}

function UndoRejectionSuccessDialog({
  reportNumber,
  onGoToJobList,
  onGoToJob,
}: {
  reportNumber: string;
  onGoToJobList: () => void;
  onGoToJob: () => void;
}) {
  return createPortal(
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="undo-rejection-success-title">
      <div className="modal-card">
        <h3 id="undo-rejection-success-title">Afvisningen er fortrudt</h3>
        <p>Sagen <strong>{reportNumber}</strong> er sendt til gennemgang igen.</p>
        <div className="modal-actions modal-actions--double">
          <button className="btn btn-secondary" type="button" onClick={onGoToJobList}>
            Til sagslisten
          </button>
          <button className="btn btn-primary" type="button" onClick={onGoToJob}>
            Til sagen
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
