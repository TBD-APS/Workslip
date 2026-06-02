import { AlertCircle, ArrowLeft, Building2, CheckCircle2, ChevronLeft, ChevronRight, FileText, Loader2, MessageSquare, Trash2 } from 'lucide-react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import type { SaveStatus, useJobDetails } from '../hooks/useJobDetails';
import { useDeleteApiJobsId } from '../../../api/generated/jobs/jobs';
import { CustomerDetailsBlock, LinkedJobsBlock, TextAreaBlock } from './JobDetailBlocks';

type JobDetailsState = ReturnType<typeof useJobDetails>;

type JobDetailsPageProps = {
  details: JobDetailsState;
  onBack: () => void;
  onDone: () => void;
};

const STEPS = [
  { icon: Building2, label: 'Sagsdetaljer' },
  { icon: FileText, label: 'Kategorier' },
  { icon: MessageSquare, label: 'Bilag' },
] as const;

export function JobDetailsPage({ details, onBack, onDone }: JobDetailsPageProps) {
  const queryClient = useQueryClient();
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

  const isLastStep = details.currentStep === STEPS.length - 1;

  return (
    <div className="page-container">
      <JobDetailsHeader
        title="Rediger sag"
        jobNumber={`SAG-${(details.job.reportNumber || details.job.id.slice(0, 4)).toUpperCase()}`}
        saveStatus={details.saveStatus}
        onBack={onBack}
        onDelete={handleDelete}
      />

      <StepIndicators currentStep={details.currentStep} onStepChange={(step) => { details.flushSave(); details.setCurrentStep(step); }} />

      {details.currentStep === 0 && (
        <>
          <CustomerDetailsBlock
            form={details.form}
            reportNumberReadOnly={details.reportNumberReadOnly}
            assignment={{
              users: details.assignableUsers,
              assignedUserIds: details.assignedUserIds,
              assignmentStatus: details.assignmentStatus,
              isLoadingUsers: details.isLoadingUsers,
              onAssignedUsersChange: details.updateAssignedUsers,
            }}
            onCustomerChange={details.updateCustomer}
            onReportNumberChange={details.updateReportNumber}
          />
          <LinkedJobsBlock
            jobs={details.linkableJobs}
            linkedJobIds={details.linkedJobIds}
            saveStatus={details.linksStatus}
            isLoading={details.isLoadingJobs}
            onChange={details.updateLinkedJobs}
          />
          <TextAreaBlock
            icon={<FileText size={18} />}
            title="Opgavebeskrivelse"
            value={details.form.taskDescription}
            onChange={details.updateTaskDescription}
            placeholder="Beskriv opgaven..."
          />
          <TextAreaBlock
            icon={<MessageSquare size={18} />}
            title="Oplysninger til kunden/tekniske observationer"
            value={details.form.customerObservations}
            onChange={details.updateCustomerObservations}
            placeholder="Notér oplysninger til kunden eller tekniske observationer..."
          />
        </>
      )}

      {details.currentStep === 1 && (
        <PlaceholderStep icon={<FileText size={18} />} title="Kategorier" text="Kategorier bygges på næste trin." />
      )}

      {details.currentStep === 2 && (
        <PlaceholderStep icon={<MessageSquare size={18} />} title="Bilag" text="Bilag bygges på næste trin." />
      )}

      <StepNavigation
        currentStep={details.currentStep}
        isLastStep={isLastStep}
        onBack={() => {
          details.flushSave();
          if (details.currentStep === 0) {
            onDone();
          } else {
            details.setCurrentStep((step) => step - 1);
          }
        }}
        onNext={() => {
          details.flushSave();
          details.setCurrentStep((step) => step + 1);
        }}
        onDone={onDone}
      />
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
        <button className="btn-icon btn-icon-danger" onClick={onDelete} aria-label="Slet sag">
          <Trash2 size={18} />
        </button>
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

type StepIndicatorsProps = {
  currentStep: number;
  onStepChange: (step: number) => void;
};

function StepIndicators({ currentStep, onStepChange }: StepIndicatorsProps) {
  return (
    <div className="step-indicators">
      {STEPS.map((step, index) => {
        const StepIcon = step.icon;
        const isActive = index === currentStep;
        const isCompleted = index < currentStep;
        return (
          <button
            key={step.label}
            className={`step-dot ${isActive ? 'active' : ''} ${isCompleted ? 'completed' : ''}`}
            onClick={() => onStepChange(index)}
            aria-label={step.label}
          >
            <StepIcon size={14} />
            <span className="step-label">{step.label}</span>
          </button>
        );
      })}
    </div>
  );
}

function PlaceholderStep({ icon, title, text }: { icon: React.ReactNode; title: string; text: string }) {
  return (
    <section className="detail-section">
      <div className="section-header-row">
        {icon}
        <h3>{title}</h3>
      </div>
      <p className="empty-state-text">{text}</p>
    </section>
  );
}

type StepNavigationProps = {
  currentStep: number;
  isLastStep: boolean;
  onBack: () => void;
  onNext: () => void;
  onDone: () => void;
};

function StepNavigation({ currentStep, isLastStep, onBack, onNext, onDone }: StepNavigationProps) {
  return (
    <div className="step-nav">
      <button
        className="step-nav-btn step-nav-btn-back"
        onClick={onBack}
        aria-label="Tilbage"
      >
        <ChevronLeft size={18} />
        <span>Tilbage</span>
      </button>

      <span className="step-nav-counter">Trin {currentStep + 1} / {STEPS.length}</span>

      {!isLastStep ? (
        <button className="step-nav-btn step-nav-btn-next" onClick={onNext}>
          <span>Næste</span>
          <ChevronRight size={18} />
        </button>
      ) : (
        <button className="step-nav-btn step-nav-btn-next" onClick={onDone}>
          <CheckCircle2 size={18} />
          <span>Færdig</span>
        </button>
      )}
    </div>
  );
}
