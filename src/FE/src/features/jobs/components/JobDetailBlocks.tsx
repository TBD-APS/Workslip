import { useCallback, useMemo, useState } from 'react';
import { Building2, FileText, Link2, Users } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { CollapsibleSection } from '../../../components/forms/CollapsibleSection';
import { SingleSelectDropdown } from '../../../components/forms/SingleSelectDropdown';
import { MultiSelectDropdown } from '../../../components/forms/MultiSelectDropdown';
import { useCan, useIsAdmin } from '../../../providers/permissions';
import { useGetApiCustomersSuggest } from '../../../api/generated/customers/customers';
import { getApiCustomersTop } from '../customerApi';
import type { CustomerSearchViewModel, CustomerSnapshotData, UserViewModel } from '../../../api/generated/models';
import type { LinkableJob } from '../types';
import { useDebounce } from '../../../hooks/useDebounce';
import { validateEmail, validatePhoneNumber } from '../../../components/forms/validators';


type CustomerBlockProps = {
  form: { customerId: string | null; customerSnapshot: CustomerSnapshotData | null; reportNumber: string };
  customerSnapshot: CustomerSnapshotData | null;
  editSnapshot: boolean;
  createCustomer?: boolean;
  onCreateCustomerChange?: (value: boolean) => void;
  hasCustomerChanges?: (snapshot: CustomerSnapshotData | null) => boolean;
  onCustomerSelect?: (customer: CustomerSearchViewModel) => void;
  onSnapshotFieldChange?: (field: keyof CustomerSnapshotData, value: string) => void;
  onEditSnapshotChange?: (edit: boolean) => void;
  showEditCheckbox: boolean;
};

type ReportNumberBlockProps = {
  value: string;
  onChange: (value: string) => void;
  readOnly?: boolean;
};

export function ReportNumberBlock({ value, onChange, readOnly = false }: ReportNumberBlockProps) {
  return (
    <section className="detail-section">
      <div className="detail-form">
        <div className="section-header-row">
          <FileText size={18} />
          <h3>Sagsnummer</h3>
        </div>
        <div className="form-group">
          <input
            className="form-input"
            value={value}
            onChange={(e) => onChange(e.target.value)}
            placeholder="Indsæt sagsnummer..."
            readOnly={readOnly}
          />
        </div>
      </div>
    </section>
  );
}

type EditCustomerCheckboxProps = {
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
};

function EditCustomerCheckbox({ checked, onChange, disabled }: EditCustomerCheckboxProps) {
  return (
    <label className={`attestation-confirm-row${disabled ? ' disabled' : ''}`}>
      <span className="attestation-confirm-copy">
        <span className="attestation-confirm-label">Rediger kunde for sag</span>
        <span className="attestation-confirm-description">
          Lås op for at redigere kundeoplysninger
        </span>
      </span>
      <input
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
      />
    </label>
  );
}

export function CustomerDetailsBlock({
  form,
  customerSnapshot,
  editSnapshot,
  createCustomer,
  onCreateCustomerChange,
  hasCustomerChanges,
  onCustomerSelect,
  onSnapshotFieldChange,
  onEditSnapshotChange,
  showEditCheckbox = true,
}: CustomerBlockProps) {
  const hasExistingCustomer = Boolean(form.customerId);
  const isAdmin = useIsAdmin();
  const [emailError, setEmailError] = useState<string | null>(null);
  const [phoneError, setPhoneError] = useState<string | null>(null);
  const showCreateCustomerCheckbox =
    hasExistingCustomer && editSnapshot && hasCustomerChanges && hasCustomerChanges(customerSnapshot);

  function displayValue(field: keyof CustomerSnapshotData): string {
    if (hasExistingCustomer) {
      const snapshotVal = customerSnapshot?.[field];
      return (snapshotVal ?? '') as string;
    }
    return '';
  }

  function handleFieldChange(field: keyof CustomerSnapshotData, value: string) {
    if (hasExistingCustomer && editSnapshot) {
      onSnapshotFieldChange?.(field, value);
    }
  }

  function isFieldReadOnly(): boolean {
    return hasExistingCustomer && !editSnapshot;
  }

  return (

    <section className="detail-section">
      <div className="detail-form">

    <div className="section-header-row">
        <Building2 size={18} />
        <h3>Kunde</h3>
      </div>

        {isAdmin && (
          <CustomerSearchDropdown
            selectedId={form.customerId}
            onSelect={onCustomerSelect}
          />
        )}

          <div className="form-group">
            <label className="form-label">Kundenavn</label>
            <input
              className="form-input"
              value={displayValue('name')}
              onChange={(e) => handleFieldChange('name', e.target.value)}
              placeholder="Kundenavn"
              readOnly={isFieldReadOnly()}
            />
          </div>
          <div className="form-group">
            <label className="form-label">Adresse</label>
            <input
              className="form-input"
              value={displayValue('address')}
              onChange={(e) => handleFieldChange('address', e.target.value)}
              placeholder="Adresse"
              readOnly={isFieldReadOnly()}
            />
          </div>
          <div className="form-group">
            <label className="form-label">Email</label>
            <input
              className={`form-input${emailError ? ' form-input-invalid' : ''}`}
              value={displayValue('email')}
              onChange={(e) => { handleFieldChange('email', e.target.value); setEmailError(null); }}
              onBlur={() => setEmailError(validateEmail(displayValue('email')))}
              placeholder="Email"
              readOnly={isFieldReadOnly()}
            />
            {emailError && <p className="form-error-text">{emailError}</p>}
          </div>
          <div className="form-group">
            <label className="form-label">Telefon</label>
            <input
              className={`form-input${phoneError ? ' form-input-invalid' : ''}`}
              value={displayValue('phone')}
              onChange={(e) => { handleFieldChange('phone', e.target.value); setPhoneError(null); }}
              onBlur={() => setPhoneError(validatePhoneNumber(displayValue('phone')))}
              placeholder="Telefon"
              readOnly={isFieldReadOnly()}
            />
            {phoneError && <p className="form-error-text">{phoneError}</p>}
          </div>
          <div className="form-group">
            <label className="form-label">Kontaktperson</label>
            <input
              className="form-input"
              value={displayValue('contactPerson')}
              onChange={(e) => handleFieldChange('contactPerson', e.target.value)}
              placeholder="Kontaktperson"
              readOnly={isFieldReadOnly()}
            />
          </div>

         {hasExistingCustomer && showEditCheckbox && (
            <EditCustomerCheckbox
              checked={editSnapshot}
              onChange={onEditSnapshotChange ?? (() => {})}
            />
          )}
          {showCreateCustomerCheckbox && (
            <label className="attestation-confirm-row">
              <span className="attestation-confirm-copy">
                <span className="attestation-confirm-label">Gem som ny kunde</span>
                <span className="attestation-confirm-description">
                  Opret en ny kunde i databasen med de ændrede oplysninger
                </span>
              </span>
              <input
                type="checkbox"
                checked={createCustomer ?? false}
                onChange={(e) => onCreateCustomerChange?.(e.target.checked)}
              />
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
  onSelect?: (customer: CustomerSearchViewModel) => void;
};

function CustomerSearchDropdown({ selectedId, onSelect }: CustomerSearchDropdownProps) {
  const [inputValue, setInputValue] = useState('');
  const debouncedQuery = useDebounce(inputValue, 300);
  const isSearching = debouncedQuery.length >= 2;

  const { data: searchResults = [], isLoading: isSearchingLoading } = useGetApiCustomersSuggest(
    { query: debouncedQuery, limit: 10 },
    { query: { enabled: isSearching } }
  );

  const { data: topCustomers = [], isLoading: isTopLoading } = useQuery({
    queryKey: ['customers', 'top'],
    queryFn: () => getApiCustomersTop({ limit: 10 }),
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

    return list;
  }, [results, selectedId,  isSearching]);

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
      label='Søg efter kunde'
      placeholder={"Vælg kunde..."}
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
