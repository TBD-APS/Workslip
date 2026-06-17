import { useCallback, useMemo, useState } from 'react';
import { Building2, FileText, Link2, Pencil, Users } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { CollapsibleSection } from '../../../components/forms/CollapsibleSection';
import { SingleSelectDropdown } from '../../../components/forms/SingleSelectDropdown';
import { MultiSelectDropdown } from '../../../components/forms/MultiSelectDropdown';
import { useCan } from '../../../providers/permissions';
import { useGetApiCustomersSuggest } from '../../../api/generated/customers/customers';
import { getApiCustomersTop } from '../customerApi';
import type { CustomerInfo, CustomerSearchViewModel, CustomerSnapshotData, UserViewModel } from '../../../api/generated/models';
import type { LinkableJob } from '../types';
import { useDebounce } from '../../../hooks/useDebounce';


type CustomerBlockProps = {
  form: { customer: CustomerInfo; reportNumber: string };
  customerSnapshot: CustomerSnapshotData | null;
  editSnapshot: boolean;
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
  onSnapshotFieldChange?: (field: keyof CustomerSnapshotData, value: string) => void;
  onEditSnapshotChange?: (edit: boolean) => void;
  onReportNumberChange: (value: string) => void;
};

export function CustomerDetailsBlock({
  form,
  customerSnapshot,
  editSnapshot,
  onCustomerSelect,
  onCustomerFieldChange,
  onSnapshotFieldChange,
  onEditSnapshotChange,
}: CustomerBlockProps) {
  const hasExistingCustomer = Boolean(form.customer.customerId);

  function displayValue(field: keyof CustomerSnapshotData): string {
    if (editSnapshot && hasExistingCustomer) {
      const snapshotVal = customerSnapshot?.[field];
      const customerVal = form.customer[field as keyof CustomerInfo];
      return (snapshotVal ?? customerVal ?? '') as string;
    }
    return (form.customer[field as keyof CustomerInfo] ?? '') as string;
  }

  function handleFieldChange(field: keyof CustomerSnapshotData, value: string) {
    if (hasExistingCustomer && editSnapshot) {
      onSnapshotFieldChange?.(field, value);
    } else if (!hasExistingCustomer) {
      onCustomerFieldChange(field as keyof CustomerInfo, value);
    }
  }

  function isFieldReadOnly(_field: keyof CustomerSnapshotData): boolean {
    return hasExistingCustomer && !editSnapshot;
  }

  return (

    <section className="detail-section">
      <div className="detail-form">

    <div className="section-header-row">
        <Building2 size={18} />
        <h3>Kunde</h3>
      </div>

        <CustomerSearchDropdown
          selectedId={form.customer.customerId}
          selectedName={form.customer.name}
          onSelect={onCustomerSelect}
        />

          <div className="form-group">
            <label className="form-label">Kundenavn</label>
            <input
              className="form-input"
              value={displayValue('name')}
              onChange={(e) => handleFieldChange('name', e.target.value)}
              placeholder="Kundenavn"
              readOnly={isFieldReadOnly('name')}
              style={isFieldReadOnly('name') ? { opacity: 0.6, cursor: 'not-allowed' } : undefined}
            />
          </div>
          <div className="form-group">
            <label className="form-label">Adresse</label>
            <input
              className="form-input"
              value={displayValue('address')}
              onChange={(e) => handleFieldChange('address', e.target.value)}
              placeholder="Adresse"
              readOnly={isFieldReadOnly('address')}
              style={isFieldReadOnly('address') ? { opacity: 0.6, cursor: 'not-allowed' } : undefined}
            />
          </div>
          <div className="form-group">
            <label className="form-label">Email</label>
            <input
              className="form-input"
              value={displayValue('email')}
              onChange={(e) => handleFieldChange('email', e.target.value)}
              placeholder="Email"
              readOnly={isFieldReadOnly('email')}
              style={isFieldReadOnly('email') ? { opacity: 0.6, cursor: 'not-allowed' } : undefined}
            />
          </div>
          <div className="form-group">
            <label className="form-label">Telefon</label>
            <input
              className="form-input"
              value={displayValue('phone')}
              onChange={(e) => handleFieldChange('phone', e.target.value)}
              placeholder="Telefon"
              readOnly={isFieldReadOnly('phone')}
              style={isFieldReadOnly('phone') ? { opacity: 0.6, cursor: 'not-allowed' } : undefined}
            />
          </div>
          <div className="form-group">
            <label className="form-label">Kontaktperson</label>
            <input
              className="form-input"
              value={form.customer.contactPerson ?? ''}
              onChange={(e) => onCustomerFieldChange('contactPerson', e.target.value)}
              placeholder="Kontaktperson"
              readOnly={hasExistingCustomer && !editSnapshot}
              style={hasExistingCustomer && !editSnapshot ? { opacity: 0.6, cursor: 'not-allowed' } : undefined}
            />
          </div>

         {hasExistingCustomer && (
            <label className="checkbox-row">
              <input
                type="checkbox"
                checked={editSnapshot}
                onChange={(e) => onEditSnapshotChange?.(e.target.checked)}
              />
              <Pencil size={14} />
              <span>Rediger kunde for sag</span>
            </label>
          )}
      </div>
    </section>
  );
}

type AssignmentBlockProps = {
  assignment?: {
    users: UserViewModel[];
    assignedUserIds: string[];
    isLoadingUsers: boolean;
    onAssignedUsersChange: (userIds: string[]) => void;
  };
  readOnlyAssigned?: { id: string; displayName: string }[];
  isEditing?: boolean;
};

export function AssignmentBlock({ assignment, readOnlyAssigned, isEditing = true }: AssignmentBlockProps) {
  const canAssign = useCan('job:assign');
  
  if (!assignment && !readOnlyAssigned) return null;

  return (
    <section className="detail-section">
      <div className="section-header-row">
        <Users size={18} />
        <h3>Tildelte medarbejdere</h3>
      </div>
      
      <div className="detail-form">
        {isEditing && assignment && canAssign ? (
          <MultiSelectDropdown
            label="Vælg medarbejdere"
            placeholder="Søg efter medarbejdere..."
            emptyText="Ingen medarbejdere fundet"
            loadingText="Henter medarbejdere..."
            options={assignment.users.map((user) => ({ id: user.id, label: user.displayName, description: user.email }))}
            selectedIds={assignment.assignedUserIds}
            isLoading={assignment.isLoadingUsers}
            icon={<Users size={16} />}
            commitOnClose
            onChange={assignment.onAssignedUsersChange}
          />
        ) : (
          <div className="form-group">
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
      <div className="section-header-row">
        <Link2 size={18}/>
        <h3>Tilføj sager</h3>
      </div>
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
  const isSearching = debouncedQuery.length >= 2;

  const { data: searchResults = [], isLoading: isSearchingLoading } = useGetApiCustomersSuggest(
    { query: debouncedQuery, limit: 10 },
    { query: { enabled: isSearching } }
  );

  const { data: topCustomers = [], isLoading: isTopLoading } = useQuery({
    queryKey: ['customers', 'top'],
    queryFn: () => getApiCustomersTop({ limit: 3 }),
    enabled: !isSearching,
  });

  const results: CustomerSearchViewModel[] = isSearching ? searchResults : topCustomers;
  const isLoading = isSearching ? isSearchingLoading : isTopLoading;

  const options = useMemo(() => {
    const list = results.map((c: CustomerSearchViewModel) => ({
      id: c.id ?? '',
      label: c.name ?? '',
      description: c.address ?? undefined,
    }));

    if (!isSearching && selectedId && selectedName && !list.some((o) => o.id === selectedId)) {
      list.unshift({
        id: selectedId,
        label: selectedName,
        description: undefined,
      });
    }

    return list;
  }, [results, selectedId, selectedName, isSearching]);

  const handleSelect = useCallback(
    (option: { id: string }) => {
      const customer = results.find((c: CustomerSearchViewModel) => c.id === option.id);
      if (customer && onSelect) {
        onSelect(customer);
      }
    },
    [results, onSelect]
  );

  return (
    <SingleSelectDropdown
      label=''
      placeholder={isSearching ? "Søger..." : "Vælg kunde..."}
      emptyText="Ingen kunder fundet"
      loadingText="Henter kunder..."
      options={options}
      selectedId={selectedId}
      isLoading={isLoading}
      footer={!isSearching && <span className="single-select-footer-text">Søg efter flere resultater...</span>}
      onSelect={handleSelect}
      onSearchChange={setInputValue}
    />
  );
}
