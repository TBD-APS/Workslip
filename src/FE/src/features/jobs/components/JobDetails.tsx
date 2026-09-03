import { useState } from 'react';
import { ArrowLeft, CheckCircle2, History, Loader2 } from 'lucide-react';
import { ErrorState } from '../../../components/ErrorState';
import { NavigationGuard } from '../../../components/forms/NavigationGuard';
import { StatusBanner } from '../../../components/StatusBanner';
import { useQueryClient } from '@tanstack/react-query';
import { notify } from '../../../lib/toast';
import type { AxiosError } from 'axios';
import type { useJobDetails } from '../hooks/useJobDetails';
import type { SaveStatus } from '../types';
import { useDeleteApiJobsId, getGetApiJobsQueryKey } from '../../../api/generated/jobs/jobs';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { DeleteButton } from '../../../components/common/DeleteButton';
import { ConfirmDeleteDialog } from '../../../components/common/ConfirmDeleteDialog';
import { useCan } from '../../../providers/permissions';
import { FeatureGate } from '../../../providers/moduleAccess';
import { formatDateLong } from '../../../lib/formatDate';
import { formatJobType } from '../../../lib/statusLabels';
import { ControlPointsStep } from './steps/ControlPointsStep';
import { JobAttestationStep } from './steps/JobAttestationStep';
import { JobCompletionStep } from './steps/JobCompletionStep';
import { JobOverviewStep } from './steps/JobOverviewStep';
import { StepIndicators, StepNavigation } from './steps/JobStepNavigation';
import { JobWorksheetsStep } from './steps/JobWorksheetsStep';
import { WorkCategoryStep } from './steps/WorkCategoryStep';
import { JOB_STEPS } from './steps/jobSteps';
import { JobHistoryDrawer } from './JobHistoryDrawer';
import { JobConversationLauncher } from './JobConversationLauncher';
import { JobStatusDots } from './JobStatusDots';
import {
  getJobStepValidationIssues,
  type JobValidationIssue,
} from '../validation/jobValidation';
import { focusValidationTarget } from '../validation/focusValidationTarget';

type JobDetailsState = ReturnType<typeof useJobDetails>;

type JobDetailsPageProps = {
  details: JobDetailsState;
  onBack: () => void;
  onDone: () => void;
  onGoToReport: (jobId: string) => void;
};

type JobDeleteErrorResponse = {
  code?: string;
  error?: string;
  message?: string;
  worksheetCount?: number;
};

export function JobDetailsPage({ details, onBack, onDone, onGoToReport }: JobDetailsPageProps) {
  const queryClient = useQueryClient();
  const canDeleteJob = useCan('job:delete');
  const [attestationConfirmed, setAttestationConfirmed] = useState(false);
  const [submission, setSubmission] = useState<{ reportNumber: string; submittedAt: Date } | null>(null);
  const [isPostSubmitting, setIsPostSubmitting] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const deleteMutation = useDeleteApiJobsId({
    mutation: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: getGetApiJobsQueryKey() });
        notify.success('Sagen er slettet');
        onDone();
      },
      onError: (error) => {
        notify.error(getJobDeleteErrorMessage(error));
      },
    },
    request: { skipGlobalErrorToast: true },
  });

  const handleDelete = () => {
    if (!canDeleteJob) return;
    if (!details.job?.id) return;

    if (details.worksheets.length > 0) {
      notify.error(getAttachedWorksheetsMessage(details.worksheets.length));
      return;
    }

    setDeleteDialogOpen(true);
  };

  const confirmDelete = () => {
    if (!details.job?.id) return;
    deleteMutation.mutate({ id: details.job.id });
  };

  const handleBack = () => {
    onBack();
  };

  if (details.isLoading) {
    return (
      <div className="page-container">
        <div className="detail-loading">
          <Loader2 className="animate-spin" size={24} />
          <p>Henter sag...</p>
        </div>
      </div>
    );
  }

  if (details.isError || !details.job) {
    return (
      <div className="page-container">
        <ErrorState message="Kunne ikke hente sagen.">
          <button className="btn btn-secondary" onClick={onBack}>
            Tilbage til oversigten
          </button>
        </ErrorState>
      </div>
    );
  }

  if (details.isSubmittingJob || isPostSubmitting) {
    return (
      <div className="page-container job-detail-page">
        <div className="job-details-header-spacer" />
        <SubmissionOverlay />
      </div>
    );
  }

  if (submission) {
    return (
      <div className="page-container job-detail-page">
        <div className="job-details-header-spacer" />
        <SubmittedConfirmation
          reportNumber={submission.reportNumber}
          submittedAt={submission.submittedAt}
          onDone={onDone}
          onGoToReport={() => details.job && onGoToReport(details.job.id)}
        />
      </div>
    );
  }

  const validationContext = {
    form: details.form,
    referenceData: details.referenceData ?? null,
    worksheetCount: details.worksheets.length,
    reportNumberReadOnly: details.reportNumberReadOnly,
  };
  const currentStepIssues = getJobStepValidationIssues(validationContext, details.currentStep);
  const currentStepIssue = currentStepIssues[0];
  const isLastStep = details.currentStep === JOB_STEPS.length - 1;
  const disableNext = currentStepIssues.length > 0;
  const nextDisabledReason = currentStepIssue?.message;
  const globalSaveStatus = getGlobalSaveStatus([
    details.saveStatus,
    details.assignmentStatus,
    details.linksStatus,
  ]);
  const stepIssues = JOB_STEPS.map((_, step) => getJobStepValidationIssues(validationContext, step));
  const completedSteps = stepIssues.map((list) => list.length === 0);
  // ONE range decides reachability: exactly the steps a click on `step` has to
  // walk. A backward move - and the step you are standing on - always lands, so
  // nothing at or behind `currentStep` is ever locked. The dot's styling, its
  // Danish aria-label/title and the bounce all read this one function, so they
  // can never name a different step than the click lands on.
  const findBlockingIssue = (step: number): JobValidationIssue | undefined =>
    step > details.currentStep
      ? stepIssues.slice(details.currentStep, step).find((list) => list.length > 0)?.[0]
      : undefined;
  const blockedReasons = JOB_STEPS.map((_, index) => findBlockingIssue(index)?.message);

  const goToValidationIssue = (validationIssue: JobValidationIssue, announce = true) => {
    if (announce) {
      notify.error(validationIssue.message, { id: 'job-actionable-validation' });
    }

    details.jumpToStep(validationIssue.step);
    window.requestAnimationFrame(() => {
      focusValidationTarget(validationIssue.targetId);
    });
  };

  // Every ordinary step move parks focus on the new step's content region, so a
  // keyboard user is not left behind the action bar they just pressed. A refused
  // move - same step, or a validation refusal - moves nothing, so it must not
  // pull focus off the pressed control onto a region still naming the old step.
  const goToStep = (step: number) => {
    if (!details.navigateToStep(step)) return;
    window.requestAnimationFrame(() => {
      document.getElementById('job-step-content')?.focus({ preventScroll: true });
    });
  };

  const handleStepChange = (nextStep: number) => {
    const blockingIssue = findBlockingIssue(nextStep);
    if (blockingIssue) {
      goToValidationIssue(blockingIssue);
      return;
    }

    goToStep(nextStep);
  };

  const canSubmitForReview =
    details.job.status === JobStatus.Draft || details.job.status === JobStatus.Rejected || details.job.status === JobStatus.Reopened;

  return (
    <div className="page-container job-detail-page">
      <NavigationGuard
        when={details.hasUnsavedChanges}
        autoSaveOnLeave={() => details.saveAllChanges({ mode: 'draft', notifyOnSuccess: true })}
        autoSavePending={details.saveStatus === 'saving'}
      />
      <JobDetailsHeader
        title="Rediger sag"
        jobNumber={`SAG-${(details.job.reportNumber || details.job.id.slice(0, 4)).toUpperCase()}`}
        jobType={details.job.jobType}
        status={details.job.status}
        enabledStatuses={canSubmitForReview ? [JobStatus.InReview] : []}
        onStatusSelect={(status) => {
          if (status === JobStatus.InReview) {
            handleStepChange(JOB_STEPS.length - 1);
          }
        }}
        onBack={handleBack}
        onDelete={canDeleteJob ? handleDelete : undefined}
        onShowHistory={() => setHistoryOpen(true)}
      />
      <div className="job-conversation-entry">
        <JobConversationLauncher
          jobId={details.job.id}
          allowSubmitForReview={canSubmitForReview}
        />
      </div>
      <StepIndicators
        currentStep={details.currentStep}
        onStepChange={handleStepChange}
        completedSteps={completedSteps}
        blockedReasons={blockedReasons}
      />

      {details.job.status === JobStatus.Rejected && (
        <StatusBanner variant="warning" title="Sagen er afvist - kontakt chef for yderligere detaljer">
          {details.job.rejectionNote && <p>{details.job.rejectionNote}</p>}
        </StatusBanner>
      )}

      <div
        id="job-step-content"
        className="job-details-content"
        tabIndex={-1}
        role="region"
        aria-label={`Trin ${details.currentStep + 1} af ${JOB_STEPS.length}: ${JOB_STEPS[details.currentStep].label}`}
      >
        {details.currentStep === 0 && (
          <JobOverviewStep details={details} />
        )}
        {details.currentStep === 1 && (
          <FeatureGate module="compliance-evidence">
            <WorkCategoryStep
              form={details.form}
              referenceData={details.referenceData}
              isLoading={details.isLoadingReferenceData}
              onCategoriesChange={details.updateWorkCategories}
              onWorkKindChange={details.updateWorkKind}
              onCustomWorkKindChange={details.updateCustomWorkKind}
            />
          </FeatureGate>
        )}
        {details.currentStep === 2 && (
          <FeatureGate module="compliance-evidence">
            <ControlPointsStep
              form={details.form}
              referenceData={details.referenceData}
              onToggleControlPoint={details.toggleControlPoint}
              onToggleCategoryIrrelevant={details.toggleCategoryIrrelevant}
              onAllIrrelevantReasonChange={details.updateAllIrrelevantReason}
            />
          </FeatureGate>
        )}
        {details.currentStep === 3 && (
          <JobWorksheetsStep
            jobId={details.job.id}
            worksheets={details.worksheets}
            totalHours={details.job.totalHours}
            totalOutlay={details.job.totalOutlay}
            assignableUsers={details.assignableUsers}
            isLoadingUsers={details.isLoadingUsers}
            isSaving={details.isSavingWorksheet}
            isDeleting={details.isDeletingWorksheet}
            onUpsert={details.upsertWorksheet}
            onDelete={details.deleteWorksheet}
            variant="list"
          />
        )}
        {details.currentStep === 4 && (
          <JobCompletionStep
            form={details.form}
            referenceData={details.referenceData}
            isLoading={details.isLoadingReferenceData}
            onClosureFlagsChange={details.updateClosureFlags}
            worksheetCount={details.worksheets.length}
          />
        )}
        {details.currentStep === 5 && (
          <JobAttestationStep
            details={details}
            confirmed={attestationConfirmed}
            onConfirmedChange={setAttestationConfirmed}
            onValidationAction={(validationIssue) => goToValidationIssue(validationIssue, false)}
            // The server assigns the sag number on the transition to InReview, so
            // the confirmation must render the number the step forwarded from the
            // submit response. `details.job` in this closure is still the
            // pre-submit draft and is empty for exactly the sager the server just
            // numbered, so it is never read here.
            onSubmitted={(reportNumber) => {
              setIsPostSubmitting(true);
              setTimeout(() => {
                setIsPostSubmitting(false);
                setSubmission({ reportNumber, submittedAt: new Date() });
              }, 1500);
            }}
          />
        )}
      </div>

      <StepNavigation
        currentStep={details.currentStep}
        isLastStep={isLastStep}
        backLabel={details.currentStep === 0 ? 'Til oversigten' : 'Tilbage'}
        onBack={() => {
          if (details.currentStep === 0) {
            handleBack();
          } else {
            goToStep(details.currentStep - 1);
          }
        }}
        onNext={() => goToStep(details.currentStep + 1)}
        onNextBlocked={currentStepIssue ? () => goToValidationIssue(currentStepIssue) : undefined}
        blockedNextLabel={currentStepIssue?.actionLabel}
        disableNext={disableNext}
        nextDisabledReason={nextDisabledReason}
        statusSlot={<SaveStatusIndicator saveStatus={globalSaveStatus} />}
        onDone={() => {}}
        hideDoneButton
      />

      <JobHistoryDrawer
        jobId={details.job.id}
        isOpen={historyOpen}
        onClose={() => setHistoryOpen(false)}
      />

      <ConfirmDeleteDialog
        open={deleteDialogOpen}
        title="Slet sag"
        message="Er du sikker på, du vil slette sagen permanent? Det kan kun lade sig gøre, hvis sagen ikke har timesedler."
        onConfirm={confirmDelete}
        onClose={() => setDeleteDialogOpen(false)}
      />
    </div>
  );
}

type HeaderProps = {
  title: string;
  jobNumber: string;
  jobType?: string;
  status: JobStatus;
  enabledStatuses?: JobStatus[];
  onStatusSelect?: (status: JobStatus) => void;
  onBack: () => void;
  onDelete?: () => void;
  onShowHistory: () => void;
};

function JobDetailsHeader({
  title,
  jobNumber,
  jobType,
  status,
  enabledStatuses,
  onStatusSelect,
  onBack,
  onDelete,
  onShowHistory,
}: HeaderProps) {
  return (
    <div className="detail-header">
      <button className="btn-icon" onClick={onBack} aria-label="Tilbage">
        <ArrowLeft size={22} />
      </button>
      <div>
        <span className="job-number">{jobNumber} &middot; {jobType && formatJobType(jobType)}</span>
        <JobStatusDots
          status={status}
          enabledStatuses={enabledStatuses}
          onStatusSelect={onStatusSelect}
        />
        <h2 className="detail-title">{title}</h2>
      </div>
      <div className="detail-header-actions">
        <button
          className="btn-icon history-btn"
          onClick={onShowHistory}
          title="Vis historik"
          aria-label="Vis historik"
        >
          <History size={20} />
        </button>
        {onDelete && <DeleteButton onClick={onDelete} ariaLabel="Slet sag" size={18} />}
      </div>
    </div>
  );
}

function SaveStatusIndicator({ saveStatus }: { saveStatus: SaveStatus }) {
  if (saveStatus === 'idle') return null;

  return (
    <div className="save-status" aria-live="polite" aria-atomic="true">
      {saveStatus === 'saving' && (
        <span className="save-indicator saving">
          <Loader2 className="animate-spin" size={14} />
          Gemmer...
        </span>
      )}
      {saveStatus === 'saved' && (
        <span className="save-indicator saved">
          <CheckCircle2 size={14} />
          Gemt
        </span>
      )}
      {saveStatus === 'error' && <span className="save-indicator error">Fejl ved gem</span>}
    </div>
  );
}

function getGlobalSaveStatus(statuses: SaveStatus[]): SaveStatus {
  if (statuses.includes('saving')) return 'saving';
  if (statuses.includes('error')) return 'error';
  if (statuses.includes('saved')) return 'saved';
  return 'idle';
}

function getJobDeleteErrorMessage(error: unknown): string {
  const data = (error as AxiosError<JobDeleteErrorResponse>).response?.data;
  if (data?.message) return data.message;

  const code = data?.code ?? data?.error;
  if (code === 'job_has_attached_worksheets') {
    return getAttachedWorksheetsMessage(data?.worksheetCount ?? 1);
  }

  return 'Kunne ikke slette sagen';
}

function getAttachedWorksheetsMessage(worksheetCount: number): string {
  const noun = worksheetCount === 1 ? 'timeseddel' : 'timesedler';
  return `Sagen kan ikke slettes, fordi den har ${worksheetCount} ${noun}. Slet ${noun} først.`;
}

type SubmittedConfirmationProps = {
  reportNumber: string;
  submittedAt: Date;
  onDone: () => void;
  onGoToReport: () => void;
};

function SubmissionOverlay() {
  return (
    <section className="detail-section submission-overlay">
      <Loader2 className="submission-overlay-spinner" size={40} />
      <h2 className="submission-overlay-title">Indsender sag</h2>
      <p className="submission-overlay-body">
        Vent et øjeblik mens sagen bliver sendt til kontoret.
      </p>
    </section>
  );
}

function SubmittedConfirmation({ reportNumber, submittedAt, onDone, onGoToReport }: SubmittedConfirmationProps) {
  return (
    <section className="detail-section submitted-confirmation">
      <div className="submitted-confirmation-icon" aria-hidden="true">
        <CheckCircle2 size={48} />
      </div>
      <h2 className="submitted-confirmation-title">Sag sendt til kontoret</h2>
      <p className="submitted-confirmation-body">
        Sag <strong>{reportNumber}</strong> er nu indsendt og klar til behandling hos kontoret.
      </p>
      <p className="submitted-confirmation-date">
        Indsendt d. {formatDateLong(submittedAt.toISOString())}
      </p>
      <button type="button" className="btn btn-secondary submitted-confirmation-button" onClick={onDone}>
        Tilbage til oversigt
      </button>
      <button type="button" className="btn btn-primary submitted-confirmation-button" onClick={onGoToReport}>
        Gå til indsendt sag
      </button>
    </section>
  );
}