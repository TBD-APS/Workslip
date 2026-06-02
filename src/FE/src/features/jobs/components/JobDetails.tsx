import { useEffect, useRef, useState } from 'react';
import { ArrowLeft, Building2, CheckCircle2, ChevronLeft, ChevronRight, FileText, Loader2, MessageSquare, AlertCircle, Users } from 'lucide-react';
import { ValidatedInput } from '../../../components/forms/ValidatedInput';
import { validateEmail, validatePhoneNumber } from '../../../components/forms/validators';
import type { CustomerInfo } from '../../../api/generated/models';
import type { AssignableUser, JobDetailsForm, SaveStatus, useJobDetails } from '../hooks/useJobDetails';

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
      />

      <StepIndicators currentStep={details.currentStep} onStepChange={details.setCurrentStep} />

      {details.currentStep === 0 && (
        <JobDetailsStep
          form={details.form}
          users={details.assignableUsers}
          assignedUserIds={details.assignedUserIds}
          assignmentStatus={details.assignmentStatus}
          isLoadingUsers={details.isLoadingUsers}
          reportNumberReadOnly={details.reportNumberReadOnly}
          onAssignedUsersChange={details.updateAssignedUsers}
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
        onBack={() => details.setCurrentStep((step) => step - 1)}
        onNext={() => details.setCurrentStep((step) => step + 1)}
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
};

function JobDetailsHeader({ title, jobNumber, saveStatus, onBack }: HeaderProps) {
  return (
    <div className="detail-header">
      <button className="btn-icon" onClick={onBack} aria-label="Tilbage">
        <ArrowLeft size={22} />
      </button>
      <div>
        <span className="job-number">{jobNumber}</span>
        <h2 className="detail-title">{title}</h2>
      </div>
      <SaveStatusIndicator saveStatus={saveStatus} />
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
  assignmentStatus: SaveStatus;
  isLoadingUsers: boolean;
  reportNumberReadOnly: boolean;
  onAssignedUsersChange: (userIds: string[]) => void;
  onCustomerChange: (field: keyof CustomerInfo, value: string | null) => void;
  onReportNumberChange: (value: string) => void;
  onTaskDescriptionChange: (value: string) => void;
  onCustomerObservationsChange: (value: string) => void;
};

function JobDetailsStep({
  form,
  users,
  assignedUserIds,
  assignmentStatus,
  isLoadingUsers,
  reportNumberReadOnly,
  onAssignedUsersChange,
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

type AssignmentBlockProps = {
  users: AssignableUser[];
  assignedUserIds: string[];
  saveStatus: SaveStatus;
  isLoading: boolean;
  onChange: (userIds: string[]) => void;
};

function AssignmentBlock({ users, assignedUserIds, saveStatus, isLoading, onChange }: AssignmentBlockProps) {
  const [isOpen, setIsOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement | null>(null);
  const selectedUsers = users.filter((user) => assignedUserIds.includes(user.id));

  useEffect(() => {
    if (!isOpen) return;

    const handlePointerDown = (event: PointerEvent) => {
      if (!dropdownRef.current?.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [isOpen]);

  const toggleUser = (userId: string) => {
    if (assignedUserIds.includes(userId)) {
      onChange(assignedUserIds.filter((id) => id !== userId));
      return;
    }

    onChange([...assignedUserIds, userId]);
  };

  return (
    <div className="assignment-field">
      <div className="assignment-field-header">
        <label className="form-label">Tildelte medarbejdere</label>
        <SaveStatusIndicator saveStatus={saveStatus} />
      </div>

      <div className="assignment-dropdown" ref={dropdownRef}>
        <button
          className="assignment-trigger"
          type="button"
          disabled={isLoading}
          onClick={() => setIsOpen((open) => !open)}
          aria-expanded={isOpen}
        >
          <span className="assignment-trigger-content">
            <Users size={16} />
            {selectedUsers.length > 0 ? `${selectedUsers.length} valgt` : 'Vælg medarbejdere'}
          </span>
          <ChevronRight className={isOpen ? 'assignment-chevron open' : 'assignment-chevron'} size={16} />
        </button>

        {isOpen && (
          <div className="assignment-menu">
            {isLoading && <p className="assignment-menu-empty">Henter medarbejdere...</p>}
            {!isLoading && users.length === 0 && <p className="assignment-menu-empty">Ingen medarbejdere fundet</p>}
            {users.map((user) => {
              const isSelected = assignedUserIds.includes(user.id);
              return (
                <button
                  key={user.id}
                  className={isSelected ? 'assignment-option selected' : 'assignment-option'}
                  type="button"
                  onClick={() => toggleUser(user.id)}
                >
                  <span className="assignment-checkbox" aria-hidden="true">
                    {isSelected && <CheckCircle2 size={14} />}
                  </span>
                  <span className="assignment-option-text">
                    <span>{user.displayName}</span>
                    <small>{user.email}</small>
                  </span>
                </button>
              );
            })}
          </div>
        )}
      </div>

      <div className="assignment-chips">
        {selectedUsers.length > 0 ? (
          selectedUsers.map((user) => (
            <button
              key={user.id}
              className="assignment-chip"
              type="button"
              onClick={() => toggleUser(user.id)}
              aria-label={`Fjern ${user.displayName}`}
            >
              <span>{user.displayName}</span>
              <span className="assignment-chip-remove" aria-hidden="true">×</span>
            </button>
          ))
        ) : (
          <span className="assignment-empty">Ingen medarbejdere valgt</span>
        )}
      </div>
    </div>
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
          <ValidatedInput label="Telefon" value={form.customer.phone} placeholder="Telefon" type="tel" validate={validatePhoneNumber} onChange={(value) => onCustomerChange('phone', value)} />
          <ValidatedInput label="Kontaktperson" value={form.customer.contactPerson} placeholder="Kontaktperson" onChange={(value) => onCustomerChange('contactPerson', value)} />
        </div>

        <AssignmentBlock
          users={users}
          assignedUserIds={assignedUserIds}
          saveStatus={assignmentStatus}
          isLoading={isLoadingUsers}
          onChange={onAssignedUsersChange}
        />
      </div>
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
    <section className="detail-section">
      <div className="section-header-row">
        {icon}
        <h3>{title}</h3>
      </div>
      <div className="form-group">
        <textarea
          className="form-input form-textarea"
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder={placeholder}
          rows={4}
        />
      </div>
    </section>
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
      {currentStep > 0 ? (
        <button className="btn btn-secondary" onClick={onBack}>
          <ChevronLeft size={18} />
          Tilbage
        </button>
      ) : (
        <div />
      )}
      {!isLastStep ? (
        <button className="btn btn-primary" onClick={onNext}>
          <span className="btn-step-label">Trin {currentStep + 1} af {STEPS.length}</span>
          Næste: {STEPS[currentStep + 1].label}
          <ChevronRight size={18} />
        </button>
      ) : (
        <button className="btn btn-primary" onClick={onDone}>
          <CheckCircle2 size={18} />
          Færdig
        </button>
      )}
    </div>
  );
}
