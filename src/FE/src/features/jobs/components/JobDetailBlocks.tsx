import { Building2, FileText, Users } from 'lucide-react';
import { CollapsibleSection } from '../../../components/forms/CollapsibleSection';
import { MultiSelectDropdown } from '../../../components/forms/MultiSelectDropdown';
import { ValidatedInput } from '../../../components/forms/ValidatedInput';
import { validateEmail, validatePhoneNumber } from '../../../components/forms/validators';
import type { CustomerInfo } from '../../../api/generated/models';
import type { AssignableUser, LinkableJob, SaveStatus } from '../types';

type CustomerBlockProps = {
  form: { customer: CustomerInfo; reportNumber: string };
  reportNumberReadOnly?: boolean;
  assignment?: {
    users: AssignableUser[];
    assignedUserIds: string[];
    assignmentStatus: SaveStatus;
    isLoadingUsers: boolean;
    onAssignedUsersChange: (userIds: string[]) => void;
  };
  onCustomerChange: (field: keyof CustomerInfo, value: string | null) => void;
  onReportNumberChange: (value: string) => void;
};

export function CustomerDetailsBlock({
  form,
  reportNumberReadOnly,
  assignment,
  onCustomerChange,
  onReportNumberChange,
}: CustomerBlockProps) {
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

        {assignment && (
          <MultiSelectDropdown
            label="Tildelte medarbejdere"
            placeholder="Vælg medarbejdere"
            emptyText="Ingen medarbejdere fundet"
            loadingText="Henter medarbejdere..."
            options={assignment.users.map((user) => ({ id: user.id, label: user.displayName, description: user.email }))}
            selectedIds={assignment.assignedUserIds}
            isLoading={assignment.isLoadingUsers}
            saveStatus={assignment.assignmentStatus}
            icon={<Users size={16} />}
            onChange={assignment.onAssignedUsersChange}
          />
        )}
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

export function LinkedJobsBlock({ jobs, linkedJobIds, saveStatus, isLoading, onChange }: LinkedJobsBlockProps) {
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

export function TextAreaBlock({ icon, title, value, placeholder, onChange }: TextAreaBlockProps) {
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
