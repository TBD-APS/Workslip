import { useState } from 'react';
import { AlertCircle, ArrowLeft, CheckCircle2, History, Loader2 } from 'lucide-react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import type { AxiosError } from 'axios';
import type { useJobDetails } from '../hooks/useJobDetails';
import type { SaveStatus } from '../types';
import { useDeleteApiJobsId } from '../../../api/generated/jobs/jobs';
import { DeleteButton } from '../../../components/common/DeleteButton';
import { useCan } from '../../../providers/permissions';
import { isValidJobForm, isValidWork } from '../utils';
import { ControlPointsStep } from './steps/ControlPointsStep';
import { validateControlPoints } from './steps/controlPointsValidation';
import { JobAttestationStep } from './steps/JobAttestationStep';
import { JobCompletionStep } from './steps/JobCompletionStep';
import { JobOverviewStep } from './steps/JobOverviewStep';
import { StepIndicators, StepNavigation } from './steps/JobStepNavigation';
import { JobWorksheetsStep } from './steps/JobWorksheetsStep';
import { WorkCategoryStep } from './steps/WorkCategoryStep';
import { JOB_STEPS } from './steps/jobSteps';
import { JobHistoryDrawer } from './JobHistoryDrawer';

type JobDetailsState = ReturnType<typeof useJobDetails>;

type JobDetailsPageProps = {
  details: JobDetailsState;
  onBack: () => void;
  onDone: () => void;
};

type JobDeleteErrorResponse = {
  code?: string;
  error?: string;
  message?: string;
  worksheetCount?: number;
};

export function JobDetailsPage({ details, onBack, onDone }: JobDetailsPageProps) {
  const queryClient = useQueryClient();
  const canDeleteJob = useCan('job:delete');
  const [attestationConfirmed, setAttestationConfirmed] = useState(false);
  const [submission, setSubmission] = useState<{ reportNumber: string; submittedAt: Date } | null>(null);
  const [historyOpen, setHistoryOpen] = useState(false);
  const deleteMutation = useDeleteApiJobsId({
    mutation: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/api/jobs'] });
        toast.success('Sagen er slettet');
        onDone();
      },
      onError: (error) => {
        toast.error(getJobDeleteErrorMessage(error));
      },
    },
  });

  const handleDelete = () => {
    if (!canDeleteJob) return;
    if (!details.job?.id) return;

    if (details.worksheets.length > 0) {
      toast.error(getAttachedWorksheetsMessage(details.worksheets.length));
      return;
    }

    if (!confirm('Slet sagen permanent? Det kan kun lade sig gøre, hvis sagen ikke har timesedler.')) return;
    deleteMutation.mutate({ id: details.job.id });
  };

  const handleBack = () => {
    details.saveCurrentStep({ validateWork: false });
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
        <div className="error-state">
          <AlertCircle size={32} />
          <p>Kunne ikke hente sagen.</p>
          <button className="btn btn-secondary" onClick={onBack}>
            Tilbage til oversigten
          </button>
        </div>
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
        />
      </div>
    );
  }

  const isLastStep = details.currentStep === JOB_STEPS.length - 1;
  const disableNext = !canAdvanceCurrentStep(details);
  const nextDisabledReason = disableNext ? getNextDisabledReason(details) : undefined;
  const globalSaveStatus = getGlobalSaveStatus([
    details.saveStatus,
    details.assignmentStatus,
    details.linksStatus,
  ]);
  const completedSteps = [
    isValidJobForm(details.form, { reportNumberReadOnly: details.reportNumberReadOnly }),
    isValidWork(details.form, details.referenceData!),
    validateControlPoints(details.form, details.referenceData!).valid,
    details.worksheets.length > 0,
  ];
  const handleStepChange = (nextStep: number) => {
    if (nextStep > 3 && details.worksheets.length === 0) {
      toast.error('Tilføj mindst én arbejdsseddel før du fortsætter');
      return;
    }

    details.navigateToStep(nextStep);
  };

  return (
    <div className="page-container job-detail-page">
      <JobDetailsHeader
        title="Rediger sag"
        jobNumber={`SAG-${(details.job.reportNumber || details.job.id.slice(0, 4)).toUpperCase()}`}
        onBack={handleBack}
        onDelete={canDeleteJob ? handleDelete : undefined}
        onShowHistory={() => setHistoryOpen(true)}
      />
      <SaveStatusIndicator saveStatus={globalSaveStatus} className="save-status-floating" />

      <StepIndicators 
        currentStep={details.currentStep} 
        onStepChange={handleStepChange} 
        completedSteps={completedSteps} 
      />

      <div className="job-details-content">
        {details.currentStep === 0 && (
          <JobOverviewStep details={details} />
        )}
        {details.currentStep === 1 && (
          <WorkCategoryStep
            form={details.form}
            referenceData={details.referenceData}
            isLoading={details.isLoadingReferenceData}
            onCategoriesChange={details.updateWorkCategories}
            onWorkKindChange={details.updateWorkKind}
            onCustomWorkKindChange={details.updateCustomWorkKind}
          />
        )}
        {details.currentStep === 2 && (
          <ControlPointsStep
            form={details.form}
            referenceData={details.referenceData}
            onToggleControlPoint={details.toggleControlPoint}
            onToggleCategoryIrrelevant={details.toggleCategoryIrrelevant}
          />
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
            onSubmitted={() => setSubmission({
              reportNumber: details.job?.reportNumber ?? '',
              submittedAt: new Date(),
            })}
          />
        )}
      </div>

      <StepNavigation
        currentStep={details.currentStep}
        isLastStep={isLastStep}
        onBack={() => {
          if (details.currentStep === 0) {
            handleBack();
          } else {
            details.navigateToStep(details.currentStep - 1);
          }
        }}
        onNext={() => details.navigateToStep(details.currentStep + 1)}
        disableNext={disableNext}
        nextDisabledReason={nextDisabledReason}
        onDone={() => {}}
        hideDoneButton
      />

      <JobHistoryDrawer 
        jobId={details.job.id} 
        isOpen={historyOpen} 
        onClose={() => setHistoryOpen(false)} 
      />
    </div>
  );
}

function canAdvanceCurrentStep(details: JobDetailsState): boolean {
  if (details.currentStep === 0) {
    return isValidJobForm(details.form, { reportNumberReadOnly: details.reportNumberReadOnly });
  }
  if (details.currentStep === 1) {
    return isValidWork(details.form, details.referenceData!);
  }
  if (details.currentStep === 2) {
    return validateControlPoints(details.form, details.referenceData!).valid;
  }
  if (details.currentStep === 3) {
    return details.worksheets.length > 0;
  }
  return true;
}

function getNextDisabledReason(details: JobDetailsState): string | undefined {
  if (details.currentStep === 0) return 'Udfyld venligst stamdata';
  if (details.currentStep === 1) return 'Vælg venligst anlægstype';
  if (details.currentStep === 2) return 'Udfyld venligst alle påkrævede kontrolpunkter';
  if (details.currentStep === 3) return 'Tilføj venligst mindst én timeseddel';
  return undefined;
}

type HeaderProps = {
  title: string;
  jobNumber: string;
  onBack: () => void;
  onDelete?: () => void;
  onShowHistory: () => void;
};

function JobDetailsHeader({ title, jobNumber, onBack, onDelete, onShowHistory }: HeaderProps) {
  return (
    <div className="detail-header">
      <button className="btn-icon" onClick={onBack} aria-label="Tilbage">
        <ArrowLeft size={22} />
      </button>
      <div>
        <span className="job-number">{jobNumber}</span>
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

function SaveStatusIndicator({ saveStatus, className }: { saveStatus: SaveStatus; className?: string }) {
  if (saveStatus === 'idle') return null;

  return (
    <div className={['save-status', className].filter(Boolean).join(' ')} aria-live="polite" aria-atomic="true">
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

const SUBMITTED_DATE_FORMATTER = new Intl.DateTimeFormat('da-DK', { day: 'numeric', month: 'long', year: 'numeric' });

type SubmittedConfirmationProps = {
  reportNumber: string;
  submittedAt: Date;
  onDone: () => void;
};

function SubmittedConfirmation({ reportNumber, submittedAt, onDone }: SubmittedConfirmationProps) {
  return (
    <section className="detail-section submitted-confirmation">
      <div className="submitted-confirmation-icon" aria-hidden="true">
        <CheckCircle2 size={48} />
      </div>
      <h2 className="submitted-confirmation-title">Sag indsendt</h2>
      <p className="submitted-confirmation-body">
        Du har indsendt sag {reportNumber} til kontoret d. {SUBMITTED_DATE_FORMATTER.format(submittedAt)}.
      </p>
      <button type="button" className="btn btn-primary submitted-confirmation-button" onClick={onDone}>
        Tilbage til oversigt
      </button>
    </section>
  );
}
