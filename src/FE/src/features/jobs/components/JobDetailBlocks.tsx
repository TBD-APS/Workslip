import { useCallback, useMemo, useState } from 'react';
import { Building2, FileText, Link2, Lock, Navigation, Star, Users } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { CollapsibleSection } from '../../../components/forms/CollapsibleSection';
import { SingleSelectDropdown, type SingleSelectOption } from '../../../components/forms/SingleSelectDropdown';
import { MultiSelectDropdown } from '../../../components/forms/MultiSelectDropdown';
import { useCan, useIsAdmin } from '../../../providers/permissions';
import { useGetApiCustomersSuggest } from '../../../api/generated/customers/customers';
import { getApiCustomersTop } from '../customerApi';
import type { CustomerSearchViewModel, CustomerSnapshotData, UserViewModel } from '../../../api/generated/models';
import type { LinkableJob } from '../types';
import { useDebounce } from '../../../hooks/useDebounce';
import { validateEmail, validatePhoneNumber } from '../../../components/forms/validators';
import { type AddressSuggestion } from '../hooks/useAddressAutocomplete';
import { AddressAutocomplete } from './AddressAutocomplete';

function getMapsUrl(address: string, zipCode: string, city: string): string | null {
  const parts = [address, zipCode, city].filter((p) => p.trim().length > 0);
  if (parts.length === 0) return null;
  return `https://maps.google.com/?q=${encodeURIComponent(parts.join(', '))}`;
}

type CustomerBlockProps = {
  form: { customerId: string | null; customerSnapshot: CustomerSnapshotData | null; reportNumber: string };
  customerSnapshot: CustomerSnapshotData | null;
  editSnapshot: boolean;
  createCustomer?: boolean;
  onCreateCustomerChange?: (value: boolean) => void;
  hasCustomerChanges?: (snapshot: CustomerSnapshotData | null) => boolean;
  onCustomerSelect?: (customer: CustomerSearchViewModel) => void;
  onCreateNewCustomer?: () => void;
  onSnapshotFieldChange?: (field: keyof CustomerSnapshotData, value: string) => void;
  onEditSnapshotChange?: (edit: boolean) => void;
  showEditCheckbox: boolean;
  fieldErrors?: Record<string, string>;
};

type DestinationAddressBlockProps = {
  value: string;
  zipCode: string;
  city: string;
  onChange: (value: string) => void;
  onZipCodeChange: (value: string) => void;
  onCityChange: (value: string) => void;
  required?: boolean;
  error?: string;
};

export function DestinationAddressBlock({ value, zipCode, city, onChange, onZipCodeChange, onCityChange, required, error }: DestinationAddressBlockProps) {
  const displayValue = useMemo(() => {
    if (zipCode && city && value) return `${value}, ${zipCode} ${city}`;
    if (value) return value;
    return '';
  }, [value, zipCode, city]);

  const handleTextChange = useCallback((text: string) => {
    onChange(text);
    onZipCodeChange('');
    onCityChange('');
  }, [onChange, onZipCodeChange, onCityChange]);

  const handleSelect = useCallback((suggestion: AddressSuggestion) => {
    onChange(suggestion.street);
    onZipCodeChange(suggestion.zipCode);
    onCityChange(suggestion.city);
  }, [onChange, onZipCodeChange, onCityChange]);

  const handleClear = useCallback(() => {
    onChange('');
    onZipCodeChange('');
    onCityChange('');
  }, [onChange, onZipCodeChange, onCityChange]);

  return (
    <section className="detail-section">
      <div className="detail-form">
        <div className="section-header-row">
          <FileText size={18} />
          <h3>Adresse (destination){required && <span className="required-asterisk">*</span>}</h3>
          {(() => {
            const mapsUrl = getMapsUrl(value, zipCode, city);
            return mapsUrl ? (
              <a
                href={mapsUrl}
                className="nav-maps-link"
                title="Åbn i Google Maps"
                onClick={(e) => e.stopPropagation()}
              >
                <Navigation size={16} />
              </a>
            ) : null;
          })()}
        </div>
        <AddressAutocomplete
          value={displayValue}
          error={error}
          required={required}
          placeholder="Søg adresse..."
          onTextChange={handleTextChange}
          onSelectSuggestion={handleSelect}
          onClear={handleClear}
        />
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
  onCreateNewCustomer,
  onSnapshotFieldChange,
  onEditSnapshotChange,
  showEditCheckbox = true,
  fieldErrors = {},
}: CustomerBlockProps) {
  const hasExistingCustomer = Boolean(form.customerId);
  const isAdmin = useIsAdmin();
  const [emailError, setEmailError] = useState<string | null>(null);
  const [phoneError, setPhoneError] = useState<string | null>(null);
  const showPicker = !hasExistingCustomer || editSnapshot;
  const showCreateCustomerCheckbox =
    hasExistingCustomer && editSnapshot && hasCustomerChanges && hasCustomerChanges(customerSnapshot);

  function displayValue(field: keyof CustomerSnapshotData): string {
    const snapshotVal = customerSnapshot?.[field];
    return (snapshotVal ?? '') as string;
  }

  function handleFieldChange(field: keyof CustomerSnapshotData, value: string) {
    if (editSnapshot || !hasExistingCustomer) {
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
        {isFieldReadOnly() && <Lock size={14} className="readonly-indicator" />}
      </div>

        {isAdmin && (
          <div className={`customer-search-slot${showPicker ? ' is-open' : ''}`}>
            <CustomerSearchDropdown
              selectedId={form.customerId}
              onSelect={onCustomerSelect}
              onCreateNew={onCreateNewCustomer}
            />
          </div>
        )}

          <div className="form-group" data-field-error="customerName">
            <label className="form-label">Kundenavn<span className="required-asterisk">*</span></label>
            <input
              className={`form-input${fieldErrors.customerName ? ' form-input-invalid' : ''}`}
              value={displayValue('name')}
              onChange={(e) => handleFieldChange('name', e.target.value)}
              placeholder="Kundenavn"
              readOnly={isFieldReadOnly()}
            />
            {fieldErrors.customerName && <p className="form-error-text">{fieldErrors.customerName}</p>}
          </div>
          <div className="form-group">
            <label className="form-label">Adresse</label>
            <AddressAutocomplete
              value={displayValue('address')}
              placeholder="Adresse"
              readOnly={isFieldReadOnly()}
              onTextChange={(text) => handleFieldChange('address', text)}
              onSelectSuggestion={(s) => handleFieldChange('address', s.display)}
            />
          </div>
          <div className="form-group" data-field-error="email">
            <label className="form-label">Email<span className="required-asterisk">*</span></label>
            <input
              className={`form-input${emailError || fieldErrors.email ? ' form-input-invalid' : ''}`}
              value={displayValue('email')}
              onChange={(e) => { handleFieldChange('email', e.target.value); setEmailError(null); }}
              onBlur={() => setEmailError(validateEmail(displayValue('email')))}
              placeholder="Email"
              readOnly={isFieldReadOnly()}
            />
            {(emailError || fieldErrors.email) && <p className="form-error-text">{emailError || fieldErrors.email}</p>}
          </div>
          <div className="form-group" data-field-error="phone">
            <label className="form-label">Telefon<span className="required-asterisk">*</span></label>
            <input
              className={`form-input${phoneError || fieldErrors.phone ? ' form-input-invalid' : ''}`}
              value={displayValue('phone')}
              onChange={(e) => { handleFieldChange('phone', e.target.value); setPhoneError(null); }}
              onBlur={() => setPhoneError(validatePhoneNumber(displayValue('phone')))}
              placeholder="Telefon"
              readOnly={isFieldReadOnly()}
            />
            {(phoneError || fieldErrors.phone) && <p className="form-error-text">{phoneError || fieldErrors.phone}</p>}
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
            options={assignment.users.map((user) => ({ id: user.id, label: user.displayName }))}
            selectedIds={assignment.assignedUserIds}
            isLoading={assignment.isLoadingUsers}
            commitOnClose
            hideSearch
            className="assignment-variant"
            onChange={assignment.onAssignedUsersChange}
          />
        ) : (
          <div className="form-group">
            {(readOnlyAssigned && readOnlyAssigned.length > 0) ? (
              <span className="form-readonly-value">{readOnlyAssigned.map((u) => u.displayName).join(', ')}</span>
            ) : (
              <span className="form-readonly-empty">Ingen medarbejdere tildelt</span>
            )}
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
        commitOnClose
        className="linked-jobs-variant"
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
    <CollapsibleSection icon={icon} title={title} defaultOpen={value.trim().length > 0} scrollOnOpen={false}>
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

const NEW_CUSTOMER_ID = '__new__';

type CustomerSearchDropdownProps = {
  selectedId: string | null;
  onSelect?: (customer: CustomerSearchViewModel) => void;
  onCreateNew?: () => void;
};

function CustomerSearchDropdown({ selectedId, onSelect, onCreateNew }: CustomerSearchDropdownProps) {
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
  });

  const results: CustomerSearchViewModel[] = useMemo(() => {
    if (!isSearching) return topCustomers;

    const seen = new Set<string>();
    const sorted = [...searchResults].sort((a, b) => (a.isTop === b.isTop ? 0 : a.isTop ? -1 : 1));
    const merged: CustomerSearchViewModel[] = [];

    for (const c of sorted) {
      if (!seen.has(c.id)) {
        seen.add(c.id);
        merged.push(c);
      }
    }

    for (const c of topCustomers) {
      if (!seen.has(c.id)) {
        seen.add(c.id);
        merged.push(c);
      }
    }

    return merged;
  }, [isSearching, searchResults, topCustomers]);
  const isLoading = isSearching ? isSearchingLoading : isTopLoading;

  const options = useMemo(() => {
    const list: SingleSelectOption[] = results.map((c: CustomerSearchViewModel) => ({
      id: c.id ?? '',
      label: c.name ?? '',
      description: c.address ?? undefined,
      icon: c.isTop ? <Star size={14} className="top-customer-icon" /> : undefined,
    }));

    if (!isSearching) {
      list.unshift({ id: NEW_CUSTOMER_ID, label: 'Opret ny kunde', description: 'Udfyld kundeoplysninger manuelt' });
    }

    return list;
  }, [results, isSearching]);

  const handleSelect = useCallback(
    (option: { id: string }) => {
      if (option.id === NEW_CUSTOMER_ID) {
        onCreateNew?.();
        return;
      }
      const customer = results.find((c: CustomerSearchViewModel) => c.id === option.id);
      if (customer && onSelect) {
        onSelect(customer);
      }
    },
    [results, onSelect, onCreateNew]
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
