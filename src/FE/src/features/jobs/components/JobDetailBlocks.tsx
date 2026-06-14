import { useCallback, useMemo, useState } from 'react';
import { Building2, FileText, Users } from 'lucide-react';
import { CollapsibleSection } from '../../../components/forms/CollapsibleSection';
import { SingleSelectDropdown } from '../../../components/forms/SingleSelectDropdown';
import { MultiSelectDropdown } from '../../../components/forms/MultiSelectDropdown';
import { useCan } from '../../../providers/permissions';
import { useGetApiCustomersSearch } from '../../../api/generated/customers/customers';
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
  onReportNumberChange: (value: string) => void;
};

export function CustomerDetailsBlock({
  form,
  reportNumberReadOnly,
  assignment,
  readOnlyAssigned,
  onCustomerSelect,
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
          onSelect={onCustomerSelect}
        />

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
  onSelect: (customer: CustomerSearchViewModel) => void;
};

function CustomerSearchDropdown({ selectedId, onSelect }: CustomerSearchDropdownProps) {
  const [inputValue, setInputValue] = useState('');
  const debouncedQuery = useDebounce(inputValue, 300);
  const { data: searchResults = [], isLoading } = useGetApiCustomersSearch(
    { query: debouncedQuery, limit: 10 },
    { query: { enabled: debouncedQuery.length >= 2 } }
  );

  const options = useMemo(() =>
    searchResults.map((c) => ({
      id: c.id ?? '',
      label: c.name ?? '',
      description: c.companyName ?? undefined,
    })),
    [searchResults]
  );

  const handleSelect = useCallback(
    (option: { id: string }) => {
      const customer = searchResults.find((c) => c.id === option.id);
      if (customer) {
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
