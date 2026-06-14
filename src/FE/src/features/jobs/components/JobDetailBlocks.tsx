import { useCallback, useMemo, useState } from 'react';
import { Building2, FileText, Users } from 'lucide-react';
import { CollapsibleSection } from '../../../components/forms/CollapsibleSection';
import { SingleSelectDropdown } from '../../../components/forms/SingleSelectDropdown';
import { MultiSelectDropdown } from '../../../components/forms/MultiSelectDropdown';
import { useCan } from '../../../providers/permissions';
import { useGetApiCustomersSuggest } from '../../../api/generated/customers/customers';
import type { CustomerInfo, CustomerSearchViewModel, UserViewModel } from '../../../api/generated/models';
import type { LinkableJob } from '../types';
import { useDebounce } from '../../../hooks/useDebounce';

type CustomerBlockProps = {
  form: { customer: CustomerInfo; reportNumber: string };
  reportNumberReadOnly?: boolean;
  assignment?: {
    users: UserViewModel[];
    assignedUserIds: string[];
    isLoadingUsers: boolean;
    onAssignedUsersChange: (userIds: string[]) => void;
  };
  readOnlyAssigned?: { id: string; displayName: string }[];
  onCustomerSelect?: (customer: CustomerSearchViewModel) => void;
  onCustomerFieldChange: (field: keyof CustomerInfo, value: string) => void;
  onReportNumberChange: (value: string) => void;
};

export function CustomerDetailsBlock({
  form,
  reportNumberReadOnly,
  assignment,
  readOnlyAssigned,
  onCustomerSelect,
  onCustomerFieldChange,
  onReportNumberChange,
}: CustomerBlockProps) {
  const canAssign = useCan('job:assign');

  return (
    <section className="detail-section customer-details-section">
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

        <CustomerSearchDropdown
          selectedId={form.customer.customerId}
          selectedName={form.customer.name}
          onSelect={onCustomerSelect}
        />

        <div className="form-group">
          <label className="form-label">Kundenavn</label>
          <input className="form-input" value={form.customer.name ?? ''} onChange={(e) => onCustomerFieldChange('name', e.target.value)} placeholder="Kundenavn" />
        </div>
        <div className="form-group">
          <label className="form-label">Adresse</label>
          <input className="form-input" value={form.customer.address ?? ''} onChange={(e) => onCustomerFieldChange('address', e.target.value)} placeholder="Adresse" />
        </div>
        <div className="form-group">
          <label className="form-label">Email</label>
          <input className="form-input" value={form.customer.email ?? ''} onChange={(e) => onCustomerFieldChange('email', e.target.value)} placeholder="Email" />
        </div>
        <div className="form-group">
          <label className="form-label">Telefon</label>
          <input className="form-input" value={form.customer.phone ?? ''} onChange={(e) => onCustomerFieldChange('phone', e.target.value)} placeholder="Telefon" />
        </div>
        <div className="form-group">
          <label className="form-label">Kontaktperson</label>
          <input className="form-input" value={form.customer.contactPerson ?? ''} onChange={(e) => onCustomerFieldChange('contactPerson', e.target.value)} placeholder="Kontaktperson" />
        </div>


        {assignment && canAssign && (
          <MultiSelectDropdown
            label="Tildelte medarbejdere"
            placeholder="Vælg medarbejdere"
            emptyText="Ingen medarbejdere fundet"
            loadingText="Henter medarbejdere..."
            options={assignment.users.map((user) => ({ id: user.id, label: user.displayName, description: user.email }))}
            selectedIds={assignment.assignedUserIds}
            isLoading={assignment.isLoadingUsers}
            icon={<Users size={16} />}
            commitOnClose
            onChange={assignment.onAssignedUsersChange}
          />
        )}

        {assignment && !canAssign && (
          <div className="form-group">
            <label className="form-label">Tildelte medarbejdere</label>
            <div className="form-readonly-list" aria-readonly="true">
              {(readOnlyAssigned && readOnlyAssigned.length > 0) ? (
                readOnlyAssigned.map((u) => (
                  <span key={u.id} className="form-readonly-chip">
                    <Users size={12} />
                    <span>{u.displayName}</span>
                  </span>
                ))
              ) : (
                <span className="form-readonly-empty">Ingen medarbejdere tildelt</span>
              )}
            </div>
          </div>
        )}
      </div>
    </section>
  );
}

type LinkedJobsBlockProps = {
  jobs: LinkableJob[];
  linkedJobIds: string[];
  isLoading: boolean;
  onChange: (jobIds: string[]) => void;
};

export function LinkedJobsBlock({ jobs, linkedJobIds, isLoading, onChange }: LinkedJobsBlockProps) {
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
        icon={<FileText size={16} />}
        commitOnClose
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
    <CollapsibleSection icon={icon} title={title} defaultOpen={value.trim().length > 0}>
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

type CustomerSearchDropdownProps = {
  selectedId: string | null;
  selectedName?: string | null;
  onSelect?: (customer: CustomerSearchViewModel) => void;
};

function CustomerSearchDropdown({ selectedId, selectedName, onSelect }: CustomerSearchDropdownProps) {
  const [inputValue, setInputValue] = useState('');
  const debouncedQuery = useDebounce(inputValue, 300);
  const { data: searchResults = [], isLoading } = useGetApiCustomersSuggest(
    { query: debouncedQuery, limit: 10 },
    { query: { enabled: debouncedQuery.length >= 2 } }
  );

  const options = useMemo(() => {
    const list = searchResults.map((c) => ({
      id: c.id ?? '',
      label: c.name ?? '',
      description: c.address ?? undefined,
    }));

    if (selectedId && selectedName && !list.some((o) => o.id === selectedId)) {
      list.unshift({
        id: selectedId,
        label: selectedName,
        description: undefined,
      });
    }

    return list;
  }, [searchResults, selectedId, selectedName]);

  const handleSelect = useCallback(
    (option: { id: string }) => {
      const customer = searchResults.find((c) => c.id === option.id);
      if (customer && onSelect) {
        onSelect(customer);
      }
    },
    [searchResults, onSelect]
  );

  return (
    <SingleSelectDropdown
      label="Kunde"
      placeholder="Vælg kunde..."
      emptyText="Ingen kunder fundet"
      loadingText="Henter kunder..."
      options={options}
      selectedId={selectedId}
      isLoading={isLoading}
      icon={<Building2 size={16} />}
      onSelect={handleSelect}
      onSearchChange={setInputValue}
    />
  );
}
