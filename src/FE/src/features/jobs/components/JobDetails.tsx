import { AlertCircle, ArrowLeft, Building2, CheckCircle2, ChevronLeft, ChevronRight, FileText, Loader2, MessageSquare, Trash2, Users } from 'lucide-react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { CollapsibleSection } from '../../../components/forms/CollapsibleSection';
import { MultiSelectDropdown } from '../../../components/forms/MultiSelectDropdown';
import { ValidatedInput } from '../../../components/forms/ValidatedInput';
import { validateEmail, validatePhoneNumber } from '../../../components/forms/validators';
import type { CustomerInfo } from '../../../api/generated/models';
import type { AssignableUser, JobDetailsForm, LinkableJob, SaveStatus, useJobDetails } from '../hooks/useJobDetails';
import { useDeleteApiJobsId } from '../../../api/generated/jobs/jobs';

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
        <JobDetailsStep
          form={details.form}
          users={details.assignableUsers}
          assignedUserIds={details.assignedUserIds}
          linkableJobs={details.linkableJobs}
          linkedJobIds={details.linkedJobIds}
          assignmentStatus={details.assignmentStatus}
          linksStatus={details.linksStatus}
          isLoadingUsers={details.isLoadingUsers}
          isLoadingJobs={details.isLoadingJobs}
          reportNumberReadOnly={details.reportNumberReadOnly}
          onAssignedUsersChange={details.updateAssignedUsers}
          onLinkedJobsChange={details.updateLinkedJobs}
          onCustomerChange={details.updateCustomer}
          onReportNumberChange={details.updateReportNumber}
          onTaskDescriptionChange={details.updateTaskDescription}
          onCustomerObservationsChange={details.updateCustomerObservations}
        />
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

type JobDetailsStepProps = {
  form: JobDetailsForm;
  users: AssignableUser[];
  assignedUserIds: string[];
  linkableJobs: LinkableJob[];
  linkedJobIds: string[];
  assignmentStatus: SaveStatus;
  linksStatus: SaveStatus;
  isLoadingUsers: boolean;
  isLoadingJobs: boolean;
  reportNumberReadOnly: boolean;
  onAssignedUsersChange: (userIds: string[]) => void;
  onLinkedJobsChange: (jobIds: string[]) => void;
  onCustomerChange: (field: keyof CustomerInfo, value: string | null) => void;
  onReportNumberChange: (value: string) => void;
  onTaskDescriptionChange: (value: string) => void;
  onCustomerObservationsChange: (value: string) => void;
};

function JobDetailsStep({
  form,
  users,
  assignedUserIds,
  linkableJobs,
  linkedJobIds,
  assignmentStatus,
  linksStatus,
  isLoadingUsers,
  isLoadingJobs,
  reportNumberReadOnly,
  onAssignedUsersChange,
  onLinkedJobsChange,
  onCustomerChange,
  onReportNumberChange,
  onTaskDescriptionChange,
  onCustomerObservationsChange,
}: JobDetailsStepProps) {
  return (
    <>
      <CustomerDetailsBlock
        form={form}
        users={users}
        assignedUserIds={assignedUserIds}
        assignmentStatus={assignmentStatus}
        isLoadingUsers={isLoadingUsers}
        reportNumberReadOnly={reportNumberReadOnly}
        onAssignedUsersChange={onAssignedUsersChange}
        onCustomerChange={onCustomerChange}
        onReportNumberChange={onReportNumberChange}
      />
      <LinkedJobsBlock
        jobs={linkableJobs}
        linkedJobIds={linkedJobIds}
        saveStatus={linksStatus}
        isLoading={isLoadingJobs}
        onChange={onLinkedJobsChange}
      />
      <TextAreaBlock
        icon={<FileText size={18} />}
        title="Opgavebeskrivelse"
        value={form.taskDescription}
        onChange={onTaskDescriptionChange}
        placeholder="Beskriv opgaven..."
      />
      <TextAreaBlock
        icon={<MessageSquare size={18} />}
        title="Oplysninger til kunden/tekniske observationer"
        value={form.customerObservations}
        onChange={onCustomerObservationsChange}
        placeholder="Notér oplysninger til kunden eller tekniske observationer..."
      />
    </>
  );
}

type CustomerDetailsBlockProps = {
  form: JobDetailsForm;
  users: AssignableUser[];
  assignedUserIds: string[];
  assignmentStatus: SaveStatus;
  isLoadingUsers: boolean;
  reportNumberReadOnly: boolean;
  onAssignedUsersChange: (userIds: string[]) => void;
  onCustomerChange: (field: keyof CustomerInfo, value: string | null) => void;
  onReportNumberChange: (value: string) => void;
};

function CustomerDetailsBlock({
  form,
  users,
  assignedUserIds,
  assignmentStatus,
  isLoadingUsers,
  reportNumberReadOnly,
  onAssignedUsersChange,
  onCustomerChange,
  onReportNumberChange,
}: CustomerDetailsBlockProps) {
  return (
    <section className="detail-section">
      <div className="section-header-row">
        <Building2 size={18} />
        <h3>Kundeoplysninger</h3>
      </div>

      <div className="detail-form">
        <div className="form-group">
          <label className="form-label">{reportNumberReadOnly ? 'Sagsnummer (skrivebeskyttet)' : 'Sagsnummer'}</label>
          <input
            className="form-input"
            value={form.reportNumber}
            onChange={(event) => onReportNumberChange(event.target.value)}
            placeholder="F.eks. 2024-001"
            readOnly={reportNumberReadOnly}
            style={reportNumberReadOnly ? { opacity: 0.6, cursor: 'not-allowed' } : undefined}
          />
        </div>

        <ValidatedInput label="Navn" value={form.customer.name} placeholder="Kundens navn" onChange={(value) => onCustomerChange('name', value)} />
        <ValidatedInput label="Adresse" value={form.customer.address} placeholder="Kundens adresse" onChange={(value) => onCustomerChange('address', value)} />
        <ValidatedInput label="Email" value={form.customer.email} placeholder="Email-adresse" type="email" validate={validateEmail} onChange={(value) => onCustomerChange('email', value)} />

        <div className="form-row">
          <ValidatedInput label="Telefon" value={form.customer.phone} placeholder="Telefon" type="tel" inputMode="numeric" validate={validatePhoneNumber} onChange={(value) => onCustomerChange('phone', value?.replace(/\D/g, '') || null)} />
          <ValidatedInput label="Kontaktperson" value={form.customer.contactPerson} placeholder="Kontaktperson" onChange={(value) => onCustomerChange('contactPerson', value)} />
        </div>

        <MultiSelectDropdown
          label="Tildelte medarbejdere"
          placeholder="Vælg medarbejdere"
          emptyText="Ingen medarbejdere fundet"
          loadingText="Henter medarbejdere..."
          options={users.map((user) => ({ id: user.id, label: user.displayName, description: user.email }))}
          selectedIds={assignedUserIds}
          isLoading={isLoadingUsers}
          saveStatus={assignmentStatus}
          icon={<Users size={16} />}
          onChange={onAssignedUsersChange}
        />
      </div>
    </section>
  );
}

type LinkedJobsBlockProps = {
  jobs: LinkableJob[];
  linkedJobIds: string[];
  saveStatus: SaveStatus;
  isLoading: boolean;
  onChange: (jobIds: string[]) => void;
};

function LinkedJobsBlock({ jobs, linkedJobIds, saveStatus, isLoading, onChange }: LinkedJobsBlockProps) {
  return (
    <section className="detail-section">
      <MultiSelectDropdown
        label="Tilknyttede sager"
        placeholder="Vælg sager"
        emptyText="Ingen andre sager fundet"
        loadingText="Henter sager..."
        options={jobs}
        selectedIds={linkedJobIds}
        isLoading={isLoading}
        saveStatus={saveStatus}
        icon={<FileText size={16} />}
        onChange={onChange}
      />
    </section>
  );
}

type TextAreaBlockProps = {
  icon: React.ReactNode;
  title: string;
  value: string;
  placeholder: string;
  onChange: (value: string) => void;
};

function TextAreaBlock({ icon, title, value, placeholder, onChange }: TextAreaBlockProps) {
  return (
    <CollapsibleSection icon={icon} title={title} defaultOpen={false}>
      <div className="form-group">
        <textarea
          className="form-input form-textarea"
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder={placeholder}
          rows={4}
        />
      </div>
    </CollapsibleSection>
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
