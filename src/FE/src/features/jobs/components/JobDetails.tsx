import { useState } from 'react';
import { AlertCircle, ArrowLeft, CheckCircle2, Loader2 } from 'lucide-react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import type { useJobDetails } from '../hooks/useJobDetails';
import type { SaveStatus } from '../types';
import { useDeleteApiJobsId } from '../../../api/generated/jobs/jobs';
import { DeleteButton } from '../../../components/common/DeleteButton';
import { isValidJobForm, isValidWork } from '../utils';
import { ControlPointsStep, validateControlPoints } from './steps/ControlPointsStep';
import { JobAttestationStep } from './steps/JobAttestationStep';
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

  const isLastStep = details.currentStep === JOB_STEPS.length - 1;
  const disableNext = !canAdvanceCurrentStep(details);
  const handleStepChange = (nextStep: number) => {
    if (nextStep > 3 && details.worksheets.length === 0) {
      toast.error('Tilføj mindst én arbejdsseddel før du fortsætter');
      return;
    }

    if (nextStep > 4 && details.job?.status !== 'Submitted') {
      toast.error('Attestér sagen før afslutning');
      return;
    }

    details.navigateToStep(nextStep);
  };

  return (
    <div className="page-container job-detail-page">
      <JobDetailsHeader
        title="Rediger sag"
        jobNumber={`SAG-${(details.job.reportNumber || details.job.id.slice(0, 4)).toUpperCase()}`}
        saveStatus={details.saveStatus}
        onBack={handleBack}
        onDelete={handleDelete}
      />

      <StepIndicators currentStep={details.currentStep} onStepChange={handleStepChange} />

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
        <JobAttestationStep
          details={details}
          confirmed={attestationConfirmed}
          onConfirmedChange={setAttestationConfirmed}
          onSubmitted={() => {
            setAttestationConfirmed(false);
            details.navigateToStep(5);
          }}
        />
      )}

      {details.currentStep === 5 && (
        <JobCompletionStep worksheetCount={details.worksheets.length} />
      )}

      <StepNavigation
        currentStep={details.currentStep}
        isLastStep={isLastStep}
        disableNext={disableNext}
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
          if (details.currentStep === 4 && details.job?.status !== 'Submitted') {
            toast.error('Attestér sagen før afslutning');
            return;
          }
          details.navigateToStep(details.currentStep + 1);
        }}
        onDone={onDone}
      />
    </div>
  );
}

function JobCompletionStep({ worksheetCount }: { worksheetCount: number }) {
  return (
    <section className="detail-section">
      <div className="section-header-row">
        <CheckCircle2 size={18} />
        <h3>Afslutning</h3>
      </div>
      <p className="subtitle">
        Sagen har {worksheetCount} {worksheetCount === 1 ? 'arbejdsseddel' : 'arbejdssedler'} og er klar til afslutning.
      </p>
    </section>
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
    return details.job?.status === 'Submitted';
  }

  return true;
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
  return (
    <div className="save-status">
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

