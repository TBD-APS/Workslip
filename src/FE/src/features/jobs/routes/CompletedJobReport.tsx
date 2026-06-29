import { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, CheckCircle2, ChevronRight, Download, Eye, FileCheck2, History, Link2, Loader2, Pencil, Save, ShieldCheck, Timer, User, X } from 'lucide-react';
import { toast } from 'sonner';
import type {
  InstallationTypeResponse,
  JobLinkInfoResponse,
  JobReportSummaryViewModel,
  WorksheetResponse,
} from '../../../api/generated/models';
import { ErrorState } from '../../../components/ErrorState';
import { getGetApiJobsIdQueryKey, getGetApiJobsQueryKey, usePostApiJobsIdStatus } from '../../../api/generated/jobs/jobs';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { useIsAdmin } from '../../../providers/permissions/usePermissions';
import { useAuth } from '../../../providers/useAuth';
import { AssignmentBlock, CustomerDetailsBlock, LinkedJobsBlock, TextAreaBlock } from '../components/JobDetailBlocks';
import { ControlPointsStep } from '../components/steps/ControlPointsStep';
import { validateControlPoints } from '../components/steps/controlPointsValidation';
import { CollapsibleSection } from '../../../components/forms/CollapsibleSection';
import { JobCompletionStep } from '../components/steps/JobCompletionStep';
import { JobWorksheetsStep } from '../components/steps/JobWorksheetsStep';
import { WorkCategoryStep } from '../components/steps/WorkCategoryStep';
import { useJobDetailsState } from '../hooks/useJobDetails';
import { formatDateLong } from '../../../lib/formatDate';
import { formatJobStatus } from '../statusLabels';
import { createJobReportPdfPreview, downloadJobReportPdf } from '../utils/downloadJobReportPdf';
import { JobHistoryDrawer } from '../components/JobHistoryDrawer';

const NUMBER_FORMATTER = new Intl.NumberFormat('da-DK', { maximumFractionDigits: 2 });

type DetailPair = { label: string; value: string | null | undefined };
type CompletedJobDetailsState = ReturnType<typeof useJobDetailsState>;

type SelectedControlPoint = {
  id: string;
  installationType: string;
  category: string;
  name: string;
};

type IrrelevantCategory = {
  id: string;
  installationType: string;
  category: string;
};

function useIsDesktop() {
  const [isDesktop] = useState(() =>
    typeof window !== 'undefined' && window.matchMedia('(min-width: 768px)').matches
  );
  return isDesktop;
}

export const CompletedJobReport = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const location = useLocation();
  const from = (location.state as { from?: string } | undefined)?.from ?? '/app';
  const readOnly = Boolean((location.state as { readOnly?: boolean } | undefined)?.readOnly);
  const details = useJobDetailsState(id, { autoSave: false });
  const isAdmin = useIsAdmin();
  const { user } = useAuth();
  const isDesktop = useIsDesktop();
  const statusMutation = usePostApiJobsIdStatus();
  const [isDownloadingPdf, setIsDownloadingPdf] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [confirmAction, setConfirmAction] = useState<'approve' | 'reject' | null>(null);
  const [isLoadingPreview, setIsLoadingPreview] = useState(false);
  const previewUrlRef = useRef<string | null>(null);
  const job = details.job;

  const selectedControlPoints = useMemo(() => getSelectedControlPoints(job?.work.installationTypes ?? []), [job?.work.installationTypes]);
  const irrelevantCategories = useMemo(() => getIrrelevantCategories(job?.work.installationTypes ?? []), [job?.work.installationTypes]);
  const sortedWorksheets = useMemo(
    () => {
      const allWorksheets = [...(job?.worksheets ?? [])].sort((left, right) => {
        const leftName = left.userDisplayName || left.userId;
        const rightName = right.userDisplayName || right.userId;
        const byName = leftName.localeCompare(rightName, 'da-DK', { sensitivity: 'base' });
        if (byName !== 0) return byName;

        return right.workDate.localeCompare(left.workDate);
      });
      
      if (!isAdmin) {
        return allWorksheets?.filter((ws) => ws.userId === user?.id) ?? [];
      }

      return allWorksheets;
    },
    [job?.worksheets, isAdmin, user?.id],
  );

  const initialLoadDone = useRef(false);
  const editScrollDone = useRef(false);

  useEffect(() => {
    if (!job || initialLoadDone.current) return;
    initialLoadDone.current = true;

    const el = document.querySelector<HTMLElement>('.app-shell');
    if (!el) return;

    el.scrollTo(0, 0);
    requestAnimationFrame(() => el.scrollTop = 0);
  }, [job]);

  useEffect(() => {
    if (!isEditing || editScrollDone.current) return;
    editScrollDone.current = true;

    const el = document.querySelector<HTMLElement>('.app-shell');
    if (!el) return;

    el.scrollTo(0, 0);
    requestAnimationFrame(() => el.scrollTop = 0);
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
      toast.error(`Kunne ikke hente PDF for sagen ${details.form.reportNumber}`);
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
      toast.error(`Kunne ikke hente PDF for sagen ${details.form.reportNumber}`);
    } finally {
      setIsLoadingPreview(false);
    }
  };

  const handleStartEdit = () => {
    details.discardChanges();
    setIsEditing(true);
    document.querySelector<HTMLElement>('.app-shell')?.scrollTo(0, 0);
  };

  const handleCancelEdit = () => {
    details.discardChanges();
    setIsEditing(false);
    document.querySelector<HTMLElement>('.app-shell')?.scrollTo(0, 0);
  };

  const handleSaveEdit = async () => {
    const cpValidation = validateControlPoints(details.form, details.referenceData!);
    if (!cpValidation.valid) {
      toast.error(cpValidation.error ?? 'Udfyld venligst alle påkrævede kontrolpunkter');
      return;
    }

    const saved = await details.saveAllChanges();
    if (!saved) return;

    setIsEditing(false);
    document.querySelector<HTMLElement>('.app-shell')?.scrollTo(0, 0);
    toast.success(`Sagen ${details.form.reportNumber} er opdateret`);
  };

  const handleApprove = () => {
    setConfirmAction('approve');
  };

  const handleReject = () => {
    setConfirmAction('reject');
  };

  const executeConfirmAction = async () => {
    if (!job || !confirmAction) return;
    const targetStatus = confirmAction === 'approve' ? JobStatus.Approved : JobStatus.Rejected;
    try {
      const updatedJob = await statusMutation.mutateAsync({ id: job.id, data: { status: targetStatus } });
      queryClient.setQueryData(getGetApiJobsIdQueryKey(job.id), updatedJob);
      await queryClient.invalidateQueries({ queryKey: getGetApiJobsQueryKey() });
      const message = confirmAction === 'approve'
        ? `Sagen ${details.form.reportNumber} er godkendt`
        : `Sagen ${details.form.reportNumber} er afvist`;
      toast.success(message);
      setConfirmAction(null);
      navigate(from);
    } catch {
      const message = confirmAction === 'approve'
        ? `Kunne ikke godkende sagen ${details.form.reportNumber}. Prøv igen.`
        : `Kunne ikke afvise sagen ${details.form.reportNumber}. Prøv igen.`;
      toast.error(message);
      setConfirmAction(null);
    }
  };

  if (details.isLoading) {
    return (
      <div className="page-container report-overview-page">
        <div className="detail-loading">
          <Loader2 size={24} className="spin" />
          <p>Henter sagsrapport...</p>
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
    { label: 'Rapportdato', value: formatDateLong(job.observations.reportDate) },
    { label: 'Anlægstyper', value: formatInstallationTypeNames(job.work.installationTypes) },
    { label: 'Opgavetype', value: formatWorkKind(job) },
    { label: 'Afslutning', value: formatClosureFlags(job) },
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
      </div>

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
              <section className="detail-section">
                <div className="section-header-row attestation-compact-header">
                  <User size={18} />
                  <h3>Kunde</h3>
                </div>
                <DetailGrid items={customerPairs} />
              </section>
            </div>

            <section className="detail-section">
              <div className="section-header-row">
                <User size={18} />
                <h3>Medarbejdere</h3>
              </div>
              <AssignedUsers users={job.assignedUsers} />
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

            <section className="detail-section">
              <div className="section-header-row attestation-compact-header">
                <Link2 size={18} />
                <h3>Tilknyttede sager</h3>
              </div>
              <LinkedJobs links={job.links} onOpen={(linkedJobId) => navigate(`/app/completed/${linkedJobId}`, { state: { from } })} />
            </section>

            {sortedWorksheets.length > 3 ? (
            <CollapsibleSection
              icon={<Timer size={18} />}
              title={`Timesedler (${sortedWorksheets.length})`}
              defaultOpen={false}
            >
                <div className="worksheet-list-section">
                  <Worksheets worksheets={sortedWorksheets} />
                  <div className="worksheet-list-totals" aria-label="Timeseddel totaler">
                    <span><strong>{formatNumber(job.totalHours)}</strong> {formatUnit(parseNullableNumber(job.totalHours), 'time', 'timer')}</span>
                    {parseNullableNumber(job.totalOutlay) > 0 && (
                      <span><strong>{formatNumber(job.totalOutlay)}</strong> {formatUnit(parseNullableNumber(job.totalOutlay), 'udlæg', 'udlæg')}</span>
                    )}
                  </div>
                </div>
              </CollapsibleSection>
            ) : (
              <section className="detail-section worksheet-list-section">
                <div className="section-header-row attestation-compact-header">
                  <Timer size={18} />
                  <h3>Timesedler</h3>
                </div>
                <Worksheets worksheets={sortedWorksheets} />
                <div className="worksheet-list-totals" aria-label="Timeseddel totaler">
                  <span><strong>{formatNumber(job.totalHours)}</strong> {formatUnit(parseNullableNumber(job.totalHours), 'time', 'timer')}</span>
                  {parseNullableNumber(job.totalOutlay) > 0 && (
                    <span><strong>{formatNumber(job.totalOutlay)}</strong> {formatUnit(parseNullableNumber(job.totalOutlay), 'udlæg', 'udlæg')}</span>
                  )}
                </div>
              </section>
            )}

            {job.status === JobStatus.InReview && isAdmin && !readOnly && (
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
          </div>

          <CollapsibleSection
            icon={<CheckCircle2 size={18} />}
            title="Kontrolpunkter"
            defaultOpen={isDesktop}
            className="kontrolpunkter-section"
          >
            <div className="attestation-control-section compact">
              <ControlPointOverview selectedControlPoints={selectedControlPoints} irrelevantCategories={irrelevantCategories} />
            </div>
          </CollapsibleSection>
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
          onConfirm={() => void executeConfirmAction()}
          onClose={() => setConfirmAction(null)}
        />
      )}
    </div>
  );
};

type ConfirmActionDialogProps = {
  action: 'approve' | 'reject';
  reportNumber: string;
  isPending: boolean;
  onConfirm: () => void;
  onClose: () => void;
};

function ConfirmActionDialog({ action, reportNumber, isPending, onConfirm, onClose }: ConfirmActionDialogProps) {
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  const isApprove = action === 'approve';

  return createPortal(
    <div className="modal-backdrop" onClick={onClose}>
      <div
        className="modal-card"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label={isApprove ? 'Godkend sag' : 'Afvis sag'}
      >
        <h3>{isApprove ? 'Godkend sag' : 'Afvis sag'}</h3>
        <p>
          Er du sikker på, du vil {isApprove ? 'godkende' : 'afvise'} sagen <strong>{reportNumber}</strong>?
        </p>

        <div className="modal-actions" style={{ gridTemplateColumns: '1fr 1fr' }}>
          <button
            type="button"
            className={isApprove ? 'btn btn-primary' : 'btn btn-danger'}
            onClick={onConfirm}
            disabled={isPending}
          >
            {isPending && <Loader2 className="animate-spin" size={16} />}
            <span>{isPending ? (isApprove ? 'Godkender...' : 'Afviser...') : (isApprove ? 'Godkend' : 'Afvis')}</span>
          </button>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={onClose}
            disabled={isPending}
          >
            Annuller
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}

function CompletedJobEditForm({ details, onCancel, onSave }: CompletedJobEditFormProps) {
  if (!details.job) return null;

  return (
    <>
      <CustomerDetailsBlock
        form={details.form}
        customerSnapshot={details.form.customerSnapshot}
        editSnapshot={details.form.editSnapshot}
        onCustomerSelect={details.selectCustomer}
        onSnapshotFieldChange={details.updateSnapshotField}
        onEditSnapshotChange={details.updateEditSnapshot}
        showEditCheckbox={true}
      />
      <AssignmentBlock assignment={{
          users: details.assignableUsers!,
          assignedUserIds: details.assignedUserIds,
          isLoadingUsers: details.isLoadingUsers,
          onAssignedUsersChange: details.updateAssignedUsers,
        }} />

      <LinkedJobsBlock
        jobs={details.linkableJobs}
        linkedJobIds={details.linkedJobIds}
        isLoading={details.isLoadingJobs}
        onChange={details.updateLinkedJobs}
      />

      <section className="detail-section attestation-summary-section">
        <div className="section-header-row attestation-compact-header">
          <FileCheck2 size={18} />
          <h3>Observationer og noter</h3>
        </div>
        <TextAreaBlock
          icon={<FileCheck2 size={18} />}
          title="Opgave"
          value={details.form.taskDescription}
          onChange={details.updateTaskDescription}
          placeholder="Beskriv opgaven..."
        />
        <TextAreaBlock
          icon={<User size={18} />}
          title="Kundeinfo"
          value={details.form.customerObservations}
          onChange={details.updateCustomerObservations}
          placeholder="Notér oplysninger til kunden..."
        />
        <TextAreaBlock
          icon={<CheckCircle2 size={18} />}
          title="Teknisk"
          value={details.form.technicalObservations}
          onChange={details.updateTechnicalObservations}
          placeholder="Notér tekniske observationer..."
        />
      </section>

      <section className="detail-section">
        <div className="section-header-row attestation-compact-header">
          <FileCheck2 size={18} />
          <h3>Opgavetype</h3>
        </div>
        <WorkCategoryStep
          form={details.form}
          referenceData={details.referenceData}
          isLoading={details.isLoadingReferenceData}
          onCategoriesChange={details.updateWorkCategories}
          onWorkKindChange={details.updateWorkKind}
          onCustomWorkKindChange={details.updateCustomWorkKind}
          mode="work-kind"
        />
      </section>

      <section className="detail-section">
        <div className="section-header-row attestation-compact-header">
          <CheckCircle2 size={18} />
          <h3>Anlægstyper og kontrolpunkter</h3>
        </div>
        <div style={{ marginBottom: '1.5rem' }}>
          <WorkCategoryStep
            form={details.form}
            referenceData={details.referenceData}
            isLoading={details.isLoadingReferenceData}
            onCategoriesChange={details.updateWorkCategories}
            onWorkKindChange={details.updateWorkKind}
            onCustomWorkKindChange={details.updateCustomWorkKind}
            mode="categories"
          />
        </div>
        <ControlPointsStep
          form={details.form}
          referenceData={details.referenceData}
          onToggleControlPoint={details.toggleControlPoint}
          onToggleCategoryIrrelevant={details.toggleCategoryIrrelevant}
        />
      </section>

      <JobWorksheetsStep
        jobId={details.job.id}
        worksheets={details.worksheets}
        totalHours={details.job.totalHours}
        totalOutlay={details.job.totalOutlay}
        assignableUsers={details.assignableUsers!}
        isLoadingUsers={details.isLoadingUsers}
        isSaving={details.isSavingWorksheet}
        isDeleting={details.isDeletingWorksheet}
        onUpsert={details.upsertWorksheet}
        onDelete={details.deleteWorksheet}
        variant="list"
      />

      <JobCompletionStep
        form={details.form}
        referenceData={details.referenceData}
        isLoading={details.isLoadingReferenceData}
        onClosureFlagsChange={details.updateClosureFlags}
        worksheetCount={details.worksheets.length}
      />

      <div className="edit-form-bottom-actions">
        <button className="btn btn-secondary edit-form-bottom-btn" type="button" onClick={onCancel} disabled={details.saveStatus === 'saving'}>
          <X size={18} />
          Annuller
        </button>
        <button className="btn btn-primary edit-form-bottom-btn edit-form-save-btn" type="button" onClick={onSave} disabled={details.saveStatus === 'saving'}>
          {details.saveStatus === 'saving' ? <Loader2 size={18} className="spin" /> : <Save size={18} />}
          {details.saveStatus === 'saving' ? 'Gemmer...' : 'Gem ændringer'}
        </button>
      </div>
    </>
  );
}

type CompletedJobEditFormProps = {
  details: CompletedJobDetailsState;
  onCancel: () => void;
  onSave: () => void;
};

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
        <span key={user.id} className="report-overview-chip">
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
          <div className="report-overview-top-row">
            <span className="report-overview-customer">{link.linkedCustomerName || 'Ukendt kunde'}</span>
            <span className="job-number">SAG-{link.linkedReportNumber}</span>
          </div>
          <div className="report-overview-link-card-footer">
            <span className="report-overview-address">{link.linkedAddress || 'Ukendt adresse'}</span>
            <span className="btn-icon" aria-label="Åbn tilknyttet sag">
              <ChevronRight size={20} />
            </span>
          </div>
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
    <ul className="worksheet-list worksheet-list--detail report-overview-timesheet-list">
      {worksheets.map((worksheet) => {
        const hours = parseNullableNumber(worksheet.hoursWorked);
        const userName = worksheet.userDisplayName || worksheet.userId;
        return (
          <li key={worksheet.id} className="worksheet-list-item worksheet-list-item--detail">
            <div className="worksheet-list-item-main worksheet-list-item-main--detail">
              <span className="worksheet-list-item-title" title={userName}>{userName}</span>
              <span className="worksheet-list-item-subtitle worksheet-list-item-subtitle--detail">{formatDateLong(worksheet.workDate)}</span>
            </div>

            <div className="worksheet-list-item-meta">
              <div className="worksheet-list-item-badge">
                <strong>{formatNumber(hours)}</strong>
                <span>{formatUnit(hours, 'time', 'timer')}</span>
              </div>
              {worksheet.sleptOnJob && <span className="worksheet-list-item-tag">Udlæg</span>}
            </div>
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

  const groupedByInstallation = useMemo(() => {
    const installMap = new Map<string, { name: string; categories: Map<string, SelectedControlPoint[]> }>();
    for (const cp of selectedControlPoints) {
      let installGroup = installMap.get(cp.installationType);
      if (!installGroup) {
        installGroup = { name: cp.installationType, categories: new Map() };
        installMap.set(cp.installationType, installGroup);
      }
      let catItems = installGroup.categories.get(cp.category);
      if (!catItems) {
        catItems = [];
        installGroup.categories.set(cp.category, catItems);
      }
      catItems.push(cp);
    }
    return [...installMap.values()];
  }, [selectedControlPoints]);

  const irrelevantByInstallation = useMemo(() => {
    const map = new Map<string, { name: string; categories: string[] }>();
    for (const ic of irrelevantCategories) {
      let group = map.get(ic.installationType);
      if (!group) {
        group = { name: ic.installationType, categories: [] };
        map.set(ic.installationType, group);
      }
      group.categories.push(ic.category);
    }
    return [...map.values()];
  }, [irrelevantCategories]);

  return (
    <>
      {groupedByInstallation.length > 0 && (
        <div className="attestation-control-grid">
          {groupedByInstallation.map((install) => (
            <div key={install.name} className="attestation-installation-block">
              <h4 className="attestation-installation-title">{install.name}</h4>
              <div className="attestation-category-grid">
                {[...install.categories.entries()].map(([category, items]) => (
                  <div key={category} className="attestation-category-block">
                    <span className="attestation-category-label">{capitalize(category)}</span>
                    <ul className="attestation-control-list compact">
                      {items.map((cp) => (
                        <li key={cp.id}>
                          <span className="attestation-control-point-name">
                            <span className="attestation-control-point-bullet">•</span>
                            <span>{cp.name}</span>
                            <span className="attestation-control-point-check">✓</span>
                          </span>
                        </li>
                      ))}
                    </ul>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}

      {irrelevantCategories.length > 0 && (
        <div className="attestation-irrelevant-section">
          <h4 className="attestation-irrelevant-section-title">Markeret irrelevant</h4>
          <div className="attestation-control-grid">
            {irrelevantByInstallation.map((install) => (
              <div key={install.name} className="attestation-installation-block">
                <h4 className="attestation-installation-title">{install.name}</h4>
                <div className="attestation-category-grid">
                  {install.categories.map((category) => (
                    <div key={category} className="attestation-category-block attestation-category-block--muted">
                      <span className="attestation-category-label">{capitalize(category)}</span>
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
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
          name: controlPoint.name
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
