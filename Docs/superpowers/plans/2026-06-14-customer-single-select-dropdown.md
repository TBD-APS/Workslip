# Customer Single-Select Dropdown Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `CustomerAutosuggest` with a new `SingleSelectDropdown` component that reuses the existing dropdown styling pattern and shows minimal customer info (name + company).

**Architecture:** New `SingleSelectDropdown` component with server-side search, reusing `multi-select-*` CSS classes. Removes `CustomerAutosuggest` and simplifies customer selection to a single dropdown click.

**Tech Stack:** React, TypeScript, existing `multi-select-*` CSS classes, `useGetApiCustomersSearch` hook

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `FE/src/components/forms/SingleSelectDropdown.tsx` | **CREATE** | New single-select dropdown with server-side search |
| `FE/src/features/jobs/components/JobDetailBlocks.tsx` | **MODIFY** | Replace `CustomerAutosuggest` with `SingleSelectDropdown` |
| `FE/src/features/jobs/components/steps/CreateOverviewStep.tsx` | **MODIFY** | Remove `onCustomerChange` prop |
| `FE/src/features/jobs/components/steps/JobOverviewStep.tsx` | **MODIFY** | Remove `onCustomerChange` prop |
| `FE/src/features/jobs/hooks/useJobCreate.ts` | **MODIFY** | Remove `updateCustomer` function |
| `FE/src/features/jobs/hooks/useJobDetails.ts` | **MODIFY** | Remove `updateCustomer` function |
| `FE/src/components/forms/CustomerAutosuggest.tsx` | **DELETE** | Replaced by `SingleSelectDropdown` |

---

## Task 1: Create `SingleSelectDropdown` Component

**Files:**
- Create: `FE/src/components/forms/SingleSelectDropdown.tsx`

- [ ] **Step 1: Create the SingleSelectDropdown component**

```tsx
import { useCallback, useEffect, useRef, useState } from 'react';
import { ChevronRight } from 'lucide-react';

export type SingleSelectOption = {
  id: string;
  label: string;
  description?: string;
};

type SingleSelectDropdownProps = {
  label: string;
  placeholder: string;
  emptyText: string;
  loadingText: string;
  options: SingleSelectOption[];
  selectedId: string | null;
  isLoading?: boolean;
  icon?: React.ReactNode;
  onSelect: (option: SingleSelectOption) => void;
};

export function SingleSelectDropdown({
  label,
  placeholder,
  emptyText,
  loadingText,
  options,
  selectedId,
  isLoading = false,
  icon,
  onSelect,
}: SingleSelectDropdownProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const dropdownRef = useRef<HTMLDivElement | null>(null);
  const selectedOption = options.find((option) => option.id === selectedId);
  const filteredOptions = searchQuery
    ? options.filter((option) => {
        const q = searchQuery.toLowerCase();
        return option.label.toLowerCase().includes(q)
          || (option.description && option.description.toLowerCase().includes(q));
      })
    : options;

  useEffect(() => {
    if (!isOpen) return undefined;

    const handlePointerDown = (event: PointerEvent) => {
      if (!dropdownRef.current?.contains(event.target as Node)) {
        setSearchQuery('');
        (document.activeElement as HTMLElement)?.blur();
        setIsOpen(false);
      }
    };

    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [isOpen]);

  const toggleDropdown = () => {
    if (isOpen) {
      setSearchQuery('');
      setIsOpen(false);
      return;
    }
    setIsOpen(true);
  };

  const handleSelect = useCallback(
    (option: SingleSelectOption) => {
      onSelect(option);
      setSearchQuery('');
      setIsOpen(false);
    },
    [onSelect]
  );

  return (
    <div className="multi-select-field">
      <div className="multi-select-field-header">
        <label className="form-label">{label}</label>
      </div>

      <div className="multi-select-dropdown" ref={dropdownRef}>
        <button
          className="multi-select-trigger"
          type="button"
          disabled={isLoading}
          onClick={toggleDropdown}
          aria-expanded={isOpen}
        >
          <span className="multi-select-trigger-content">
            {icon}
            {selectedOption ? selectedOption.label : placeholder}
          </span>
          <ChevronRight className={isOpen ? 'multi-select-chevron open' : 'multi-select-chevron'} size={16} />
        </button>

        {isOpen && (
          <div className="multi-select-menu">
            <div className="multi-select-search">
              <input
                className="multi-select-search-input"
                type="text"
                placeholder="Søg..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                autoFocus
              />
            </div>
            {isLoading && <p className="multi-select-menu-empty">{loadingText}</p>}
            {!isLoading && filteredOptions.length === 0 && (
              <p className="multi-select-menu-empty">{searchQuery ? 'Ingen resultater' : emptyText}</p>
            )}
            {filteredOptions.map((option) => {
              const isSelected = option.id === selectedId;
              return (
                <button
                  key={option.id}
                  className={isSelected ? 'multi-select-option selection-row selected' : 'multi-select-option selection-row'}
                  type="button"
                  onClick={() => handleSelect(option)}
                  role="option"
                  aria-selected={isSelected}
                >
                  <span className="multi-select-option-text">
                    <span>{option.label}</span>
                    {option.description && <small>{option.description}</small>}
                  </span>
                </button>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Verify component compiles**

Run: `cd FE && npx tsc --noEmit src/components/forms/SingleSelectDropdown.tsx`
Expected: No errors

- [ ] **Step 3: Commit**

```bash
git add FE/src/components/forms/SingleSelectDropdown.tsx
git commit -m "feat: add SingleSelectDropdown component"
```

---

## Task 2: Update `CustomerDetailsBlock` to Use `SingleSelectDropdown`

**Files:**
- Modify: `FE/src/features/jobs/components/JobDetailBlocks.tsx`

- [ ] **Step 1: Update imports**

Replace the imports section (lines 1-9) with:

```tsx
import { useMemo, useState } from 'react';
import { Building2, FileText, Users } from 'lucide-react';
import { CollapsibleSection } from '../../../components/forms/CollapsibleSection';
import { SingleSelectDropdown } from '../../../components/forms/SingleSelectDropdown';
import { MultiSelectDropdown } from '../../../components/forms/MultiSelectDropdown';
import { useCan } from '../../../providers/permissions';
import { useGetApiCustomersSearch } from '../../../api/generated/customers/customers';
import type { CustomerInfo, CustomerSearchViewModel, UserViewModel } from '../../../api/generated/models';
import type { LinkableJob } from '../types';
```

- [ ] **Step 2: Update CustomerBlockProps**

Replace the `CustomerBlockProps` type (lines 11-24) with:

```tsx
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
```

- [ ] **Step 3: Update function signature**

Replace the function signature (lines 26-34) with:

```tsx
export function CustomerDetailsBlock({
  form,
  reportNumberReadOnly,
  assignment,
  readOnlyAssigned,
  onCustomerSelect,
  onReportNumberChange,
}: CustomerBlockProps) {
```

- [ ] **Step 4: Replace CustomerAutosuggest with SingleSelectDropdown**

Replace lines 57-66 (the `CustomerAutosuggest` section) with:

```tsx
        <CustomerSearchDropdown
          selectedId={form.customer.customerId}
          onSelect={onCustomerSelect}
        />
```

- [ ] **Step 5: Remove manual customer field inputs**

Remove lines 67-73 (the address, email, phone, contactPerson inputs). The customer details block should only show:
1. Report number input
2. Customer dropdown (SingleSelectDropdown)
3. Assignment dropdown

- [ ] **Step 6: Add CustomerSearchDropdown helper component**

Add this component at the end of the file (before the closing `}`):

```tsx
type CustomerSearchDropdownProps = {
  selectedId: string | null;
  onSelect: (customer: CustomerSearchViewModel) => void;
};

function CustomerSearchDropdown({ selectedId, onSelect }: CustomerSearchDropdownProps) {
  const [searchQuery, setSearchQuery] = useState('');
  const { data: searchResults = [], isLoading } = useGetApiCustomersSearch(
    { query: searchQuery, limit: 10 },
    { query: { enabled: searchQuery.length >= 2 } }
  );

  const options = useMemo(() =>
    searchResults.map((c) => ({
      id: c.id,
      label: c.name ?? '',
      description: c.companyName ?? undefined,
    })),
    [searchResults]
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
      onSelect={onSelect}
    />
  );
}
```

- [ ] **Step 7: Verify component compiles**

Run: `cd FE && npx tsc --noEmit src/features/jobs/components/JobDetailBlocks.tsx`
Expected: No errors

- [ ] **Step 8: Commit**

```bash
git add FE/src/features/jobs/components/JobDetailBlocks.tsx
git commit -m "feat: replace CustomerAutosuggest with SingleSelectDropdown"
```

---

## Task 3: Update Hooks to Remove `updateCustomer`

**Files:**
- Modify: `FE/src/features/jobs/hooks/useJobCreate.ts`
- Modify: `FE/src/features/jobs/hooks/useJobDetails.ts`

- [ ] **Step 1: Remove updateCustomer from useJobCreate**

Find and remove the `updateCustomer` function (lines 81-86):

```typescript
// REMOVE THIS:
const updateCustomer = (field: keyof CustomerInfo, value: string | null) => {
  setForm((prev) => ({
    ...prev,
    customer: { ...prev.customer, [field]: value },
  }));
};
```

- [ ] **Step 2: Remove updateCustomer from useJobCreate return**

Find the return statement and remove `updateCustomer` from the returned object.

- [ ] **Step 3: Remove updateCustomer from useJobDetails**

Find and remove the `updateCustomer` function (lines 285-290):

```typescript
// REMOVE THIS:
const updateCustomer = (field: keyof CustomerInfo, value: string | null) => {
  updateDraft({
    ...form,
    customer: { ...form.customer, [field]: toNullable(value) },
  });
};
```

- [ ] **Step 4: Remove updateCustomer from useJobDetails return**

Find the return statement and remove `updateCustomer` from the returned object.

- [ ] **Step 5: Verify hooks compile**

Run: `cd FE && npx tsc --noEmit src/features/jobs/hooks/useJobCreate.ts src/features/jobs/hooks/useJobDetails.ts`
Expected: No errors

- [ ] **Step 6: Commit**

```bash
git add FE/src/features/jobs/hooks/useJobCreate.ts FE/src/features/jobs/hooks/useJobDetails.ts
git commit -m "feat: remove updateCustomer from hooks (single-select only)"
```

---

## Task 4: Update Step Components

**Files:**
- Modify: `FE/src/features/jobs/components/steps/CreateOverviewStep.tsx`
- Modify: `FE/src/features/jobs/components/steps/JobOverviewStep.tsx`

- [ ] **Step 1: Remove onCustomerChange from CreateOverviewStep**

In `CreateOverviewStep.tsx`, remove line 19:
```tsx
// REMOVE THIS LINE:
onCustomerChange={create.updateCustomer}
```

- [ ] **Step 2: Remove onCustomerChange from JobOverviewStep**

In `JobOverviewStep.tsx`, remove line 31:
```tsx
// REMOVE THIS LINE:
onCustomerChange={details.updateCustomer}
```

- [ ] **Step 3: Verify step components compile**

Run: `cd FE && npx tsc --noEmit src/features/jobs/components/steps/CreateOverviewStep.tsx src/features/jobs/components/steps/JobOverviewStep.tsx`
Expected: No errors

- [ ] **Step 4: Commit**

```bash
git add FE/src/features/jobs/components/steps/CreateOverviewStep.tsx FE/src/features/jobs/components/steps/JobOverviewStep.tsx
git commit -m "feat: remove onCustomerChange from step components"
```

---

## Task 5: Delete CustomerAutosuggest

**Files:**
- Delete: `FE/src/components/forms/CustomerAutosuggest.tsx`

- [ ] **Step 1: Verify no other files import CustomerAutosuggest**

Run: `rg "CustomerAutosuggest" FE/src --include "*.tsx" --include "*.ts"`
Expected: No results (all imports should have been removed in previous tasks)

- [ ] **Step 2: Delete the file**

```bash
rm FE/src/components/forms/CustomerAutosuggest.tsx
```

- [ ] **Step 3: Verify project compiles**

Run: `cd FE && npx tsc --noEmit`
Expected: No errors

- [ ] **Step 4: Commit**

```bash
git add -A FE/src/components/forms/CustomerAutosuggest.tsx
git commit -m "feat: delete CustomerAutosuggest (replaced by SingleSelectDropdown)"
```

---

## Task 6: Final Verification

- [ ] **Step 1: Run full TypeScript check**

Run: `cd FE && npx tsc --noEmit`
Expected: No errors

- [ ] **Step 2: Run build**

Run: `cd FE && npm run build`
Expected: Build succeeds

- [ ] **Step 3: Verify no regressions in other components**

Run: `rg "onCustomerChange" FE/src --include "*.tsx" --include "*.ts"`
Expected: No results

- [ ] **Step 4: Final commit if needed**

If any fixes were needed, commit them.
