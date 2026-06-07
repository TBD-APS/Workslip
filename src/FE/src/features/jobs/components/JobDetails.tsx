import { useState } from 'react';
import { AlertCircle, AlertTriangle, ArrowLeft, CheckCircle2, Loader2 } from 'lucide-react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import type { useJobDetails } from '../hooks/useJobDetails';
import type { SaveStatus } from '../types';
import { useDeleteApiJobsId } from '../../../api/generated/jobs/jobs';
import { DeleteButton } from '../../../components/common/DeleteButton';
import { isValidJobForm, isValidWork } from '../utils';
import { ControlPointsStep, validateControlPoints } from './steps/ControlPointsStep';
import { JobAttestationStep } from './steps/JobAttestationStep';
import { JobCompletionStep } from './steps/JobCompletionStep';
import { JobOverviewStep } from './steps/JobOverviewStep';
import { StepIndicators, StepNavigation } from './steps/JobStepNavigation';
import { JobWorksheetsStep } from './steps/JobWorksheetsStep';
import { WorkCategoryStep } from './steps/WorkCategoryStep';
import { JOB_STEPS } from './steps/jobSteps';

type JobDetailsState = ReturnType<typeof useJobDetails>;

type JobDetailsPageProps = {
  details: JobDetailsState;
  onBack: () => void;
  onDone: () => void;
};

export function JobDetailsPage({ details, onBack, onDone }: JobDetailsPageProps) {
  const queryClient = useQueryClient();
  const [attestationConfirmed, setAttestationConfirmed] = useState(false);
  const [submission, setSubmission] = useState<{ reportNumber: string; submittedAt: Date } | null>(null);
  const deleteMutation = useDeleteApiJobsId({
    mutation: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/api/jobs'] });
        toast.success('Sagen er slettet');
        onDone();
      },
      onError: () => {
        toast.error('Kunne ikke slette sagen');
      },
    },
  });

  const handleDelete = () => {
    if (!details.job?.id) return;
    if (!confirm('Er du sikker på at du vil slette denne sag?')) return;
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
    isValidWork(details.form, details.referenceData),
    validateControlPoints(details.form, details.referenceData).valid,
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
        saveStatus={globalSaveStatus}
        onBack={handleBack}
        onDelete={handleDelete}
      />

      <StepIndicators currentStep={details.currentStep} onStepChange={handleStepChange} />

      <StepsCompletionPrompt
        currentStep={details.currentStep}
        completedSteps={completedSteps}
        navigateToStep={details.navigateToStep}
      />

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
          onSubmitted={() => {
            setAttestationConfirmed(false);
            setSubmission({
              reportNumber: (details.job?.reportNumber || details.job?.id.slice(0, 4) || '').toUpperCase(),
              submittedAt: new Date(),
            });
            queryClient.invalidateQueries({ queryKey: ['/api/jobs'] });
          }}
        />
      )}

      <StepNavigation
        currentStep={details.currentStep}
        isLastStep={isLastStep}
        disableNext={disableNext}
        nextDisabledReason={nextDisabledReason}
        hideDoneButton={isLastStep}
        onBack={() => {
          if (details.currentStep === 0) {
            details.flushSave();
            onDone();
          } else {
            details.navigateToStep(details.currentStep - 1);
          }
        }}
        onNext={() => {
          // Validate control points step
          if (details.currentStep === 2) {
            const validation = validateControlPoints(details.form, details.referenceData);
            if (!validation.valid) {
              toast.error(validation.error || 'Venligst validér kontrolpunkterne');
              return;
            }
          }
          if (details.currentStep === 3 && details.worksheets.length === 0) {
            toast.error('Tilføj mindst én arbejdsseddel før du fortsætter');
            return;
          }
          details.navigateToStep(details.currentStep + 1);
        }}
        onDone={onDone}
      />
    </div>
  );
}

function canAdvanceCurrentStep(details: JobDetailsState) {
  if (details.currentStep === 0) {
    return isValidJobForm(details.form, { reportNumberReadOnly: details.reportNumberReadOnly });
  }

  if (details.currentStep === 1) {
    return isValidWork(details.form, details.referenceData);
  }

  if (details.currentStep === 2) {
    return validateControlPoints(details.form, details.referenceData).valid;
  }

  if (details.currentStep === 3) {
    return details.worksheets.length > 0;
  }

  if (details.currentStep === 4) {
    return true;
  }

  if (details.currentStep === 5) {
    return details.job?.status === 'Submitted';
  }

  return true;
}

function getNextDisabledReason(details: JobDetailsState): string {
  switch (details.currentStep) {
    case 0:
      return 'Udfyld alle obligatoriske felter i sagsdetaljer før du fortsætter.';
    case 1:
      return 'Vælg mindst én arbejdskategori og et arbejdsslag før du fortsætter.';
    case 2: {
      const validation = validateControlPoints(details.form, details.referenceData);
      return validation.error ?? 'Kontrolpunkter kræver din handling før du fortsætter.';
    }
    case 3:
      return 'Tilføj mindst én arbejdsseddel før du fortsætter.';
    case 5:
      return 'Indsend sagen fra attestering før du afslutter.';
    default:
      return 'Næste trin er ikke tilgængeligt endnu.';
  }
}

type StepsCompletionPromptProps = {
  currentStep: number;
  completedSteps: boolean[];
  navigateToStep: (step: number) => void;
};

function StepsCompletionPrompt({ currentStep, completedSteps, navigateToStep }: StepsCompletionPromptProps) {
  if (currentStep === 0) return null;

  const incompleteSteps = completedSteps
    .map((isValid, index) => ({ index, label: JOB_STEPS[index].label, isValid }))
    .filter((s) => !s.isValid && s.index < currentStep);

  if (incompleteSteps.length === 0) return null;

  const isAfslutning = currentStep === 4;
  const title = isAfslutning
    ? 'Nogle trin kræver din handling før sagen kan afsluttes:'
    : 'Følgende tidligere trin kræver din handling:';

  return (
    <div className="invalid-steps-warning">
      <p className="warning-title">
        <AlertTriangle size={16} />
        {title}
      </p>
      <div className="invalid-steps-links">
        {incompleteSteps.map((step) => (
          <button
            key={step.index}
            type="button"
            className="btn btn-secondary invalid-step-btn"
            onClick={() => navigateToStep(step.index)}
          >
            Gå til {step.label}
          </button>
        ))}
      </div>
    </div>
  );
}

type HeaderProps = {
  title: string;
  jobNumber: string;
  saveStatus: SaveStatus;
  onBack: () => void;
  onDelete: () => void;
};

function JobDetailsHeader({ title, jobNumber, saveStatus, onBack, onDelete }: HeaderProps) {
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
        <SaveStatusIndicator saveStatus={saveStatus} />
        <DeleteButton onClick={onDelete} ariaLabel="Slet sag" size={18} />
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

const SUBMITTED_DATE_FORMATTER = new Intl.DateTimeFormat('da-DK', { day: '2-digit', month: '2-digit', year: 'numeric' });

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

