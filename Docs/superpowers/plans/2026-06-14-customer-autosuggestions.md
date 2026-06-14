# Customer Autosuggestions for Job Reports

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement customer autosuggestions when creating/editing a job report, with editable customer fields on the job. `CustomerId` links to master customer for reference; snapshot fields (`CustomerName`, `CustomerEmail`, `CustomerPhone`, `CustomerAddress`) store per-job copies that are independently editable.

**Architecture:** Add customer snapshot columns to `JobReports`, add a customer search endpoint, update create/update logic to persist snapshots, update the mapper to return snapshot data, add a `useDebounce` hook, and add an autosuggest component to the frontend. Migration skipped for now.

**Tech Stack:** C# / ASP.NET Minimal API / EF Core / FluentValidation / React / TypeScript / Orval codegen

---

## File Structure

### Backend
| File | Action | Purpose |
|------|--------|---------|
| `Workslip.Domain/Models/JobReportRow.cs` | Modify | Add snapshot fields |
| `Workslip.Infrastructure/Schema/SqlDbContext.cs` | Modify | EF config for snapshot columns |
| `Workslip.Application/Customers/ICustomerRepository.cs` | Modify | Add `SearchAsync` |
| `Workslip.Application/Customers/ICustomerService.cs` | Modify | Add `SearchAsync` |
| `Workslip.Application/Customers/CustomerService.cs` | Modify | Implement `SearchAsync` |
| `Workslip.Infrastructure/Repositories/EfCustomerRepository.cs` | Modify | Implement `SearchAsync` |
| `Workslip.Application/Customers/CustomerContracts.cs` | Modify | Add `CustomerSearchResponse` |
| `Workslip.Api/Endpoints/CustomerEndpoints.cs` | Modify | Add search endpoint |
| `Workslip.Api/ViewModels/CustomerViewModels.cs` | Modify | Add search view model |
| `Workslip.Infrastructure/Repositories/EfJobRepository.cs` | Modify | Store snapshots on create/update |
| `Workslip.Infrastructure/Mappers/JobReportMapper.cs` | Modify | Map snapshot fields to response |

### Frontend
| File | Action | Purpose |
|------|--------|---------|
| `FE/src/api/generated/customers/customers.ts` | Regenerate | Orval generates search hook |
| `FE/src/hooks/useDebounce.ts` | Create | Reusable debounce hook |
| `FE/src/components/forms/CustomerAutosuggest.tsx` | Create | Autosuggest component |
| `FE/src/features/jobs/components/JobDetailBlocks.tsx` | Modify | Use autosuggest on name field |
| `FE/src/features/jobs/hooks/useJobCreate.ts` | Modify | Pass customerId in save request |
| `FE/src/features/jobs/hooks/useJobDetails.ts` | Modify | Add selectCustomer helper |

---

## Task 1: Add Snapshot Fields to JobReportRow

**Files:**
- Modify: `BE/WorkslipApi/Workslip.Domain/Models/JobReportRow.cs`

- [ ] **Step 1: Add snapshot properties**

```csharp
public string? CustomerName { get; init; }
public string? CustomerEmail { get; init; }
public string? CustomerPhone { get; init; }
public string? CustomerAddress { get; init; }
```

Add these after line 10 (`public CustomerRow? CustomerRow { get; set; }`).

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build BE/WorkslipApi/Workslip.slnx`

---

## Task 2: Configure EF Core for Snapshot Columns

**Files:**
- Modify: `BE/WorkslipApi/Workslip.Infrastructure/Schema/SqlDbContext.cs`

- [ ] **Step 1: Add snapshot column configuration**

In the `ConfigureJobReports` method (line 246), after the existing property configurations (after `entity.Property(e => e.Remarks).HasColumnType("nvarchar(max)");`), add:

```csharp
entity.Property(e => e.CustomerName).HasMaxLength(200);
entity.Property(e => e.CustomerEmail).HasMaxLength(320);
entity.Property(e => e.CustomerPhone).HasMaxLength(50);
entity.Property(e => e.CustomerAddress).HasMaxLength(500);
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build BE/WorkslipApi/Workslip.slnx`

---

## Task 3: Add CustomerSearchResponse Contract

**Files:**
- Modify: `BE/WorkslipApi/Workslip.Application/Customers/CustomerContracts.cs`

- [ ] **Step 1: Add search response record**

```csharp
public sealed record CustomerSearchResponse(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    string? Address);
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build BE/WorkslipApi/Workslip.slnx`

---

## Task 4: Add SearchAsync to ICustomerRepository

**Files:**
- Modify: `BE/WorkslipApi/Workslip.Application/Customers/ICustomerRepository.cs`

- [ ] **Step 1: Add SearchAsync method**

```csharp
Task<IReadOnlyList<CustomerSearchResponse>> SearchAsync(Guid organizationId, string query, int limit, CancellationToken cancellationToken);
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build BE/WorkslipApi/Workslip.slnx`

---

## Task 5: Implement SearchAsync in EfCustomerRepository

**Files:**
- Modify: `BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfCustomerRepository.cs`

- [ ] **Step 1: Implement SearchAsync**

Add at the end of the class, before the closing brace:

```csharp
public async Task<IReadOnlyList<CustomerSearchResponse>> SearchAsync(Guid organizationId, string query, int limit, CancellationToken cancellationToken)
{
    var trimmed = query.Trim();

    var customers = await _dbContext.Customers
        .AsNoTracking()
        .Where(c => c.OrganizationId == organizationId)
        .Where(c =>
            (c.Name != null && c.Name.Contains(trimmed)) ||
            (c.Email != null && c.Email.Contains(trimmed)) ||
            (c.Phone != null && c.Phone.Contains(trimmed)) ||
            (c.Address != null && c.Address.Contains(trimmed)))
        .OrderBy(c => c.Name != null && c.Name.StartsWith(trimmed) ? 0 : 1)
        .ThenBy(c => c.Name)
        .Take(limit)
        .Select(c => new CustomerSearchResponse(
            c.Id,
            c.Name,
            c.Email,
            c.Phone,
            c.Address))
        .ToListAsync(cancellationToken);

    return customers;
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build BE/WorkslipApi/Workslip.slnx`

---

## Task 6: Add SearchAsync to ICustomerService and CustomerService

**Files:**
- Modify: `BE/WorkslipApi/Workslip.Application/Customers/ICustomerService.cs`
- Modify: `BE/WorkslipApi/Workslip.Application/Customers/CustomerService.cs`

- [ ] **Step 1: Add to interface**

```csharp
Task<Result<IReadOnlyList<CustomerSearchResponse>>> SearchAsync(string? query, int? limit, CancellationToken cancellationToken);
```

- [ ] **Step 2: Implement in service**

```csharp
public async Task<Result<IReadOnlyList<CustomerSearchResponse>>> SearchAsync(string? query, int? limit, CancellationToken cancellationToken)
{
    var organizationId = currentUser.OrganizationId;
    if (organizationId is null)
    {
        logger.LogWarning("Customer search requested without OrganizationId in claims.");
        return Result<IReadOnlyList<CustomerSearchResponse>>.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(query))
    {
        return Result<IReadOnlyList<CustomerSearchResponse>>.Success(Array.Empty<CustomerSearchResponse>());
    }

    var normalizedLimit = Math.Clamp(limit ?? 10, 1, 25);
    var customers = await customerRepository.SearchAsync(organizationId.Value, query, normalizedLimit, cancellationToken);
    return Result<IReadOnlyList<CustomerSearchResponse>>.Success(customers);
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build BE/WorkslipApi/Workslip.slnx`

---

## Task 7: Add Customer Search View Model

**Files:**
- Modify: `BE/WorkslipApi/ViewModels/CustomerViewModels.cs`

- [ ] **Step 1: Add search view model**

```csharp
public sealed record CustomerSearchViewModel(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    string? Address);
```

- [ ] **Step 2: Add builder method**

```csharp
public static CustomerSearchViewModel ToSearch(CustomerSearchResponse customer) => new(
    customer.Id,
    customer.Name,
    customer.Email,
    customer.Phone,
    customer.Address);
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build BE/WorkslipApi/Workslip.slnx`

---

## Task 8: Add Customer Search Endpoint

**Files:**
- Modify: `BE/WorkslipApi/Endpoints/CustomerEndpoints.cs`

- [ ] **Step 1: Add search endpoint**

The customer endpoints currently require `RequireAdmin`. The search endpoint should be available to all authenticated users (for job create/edit). Add a separate group without the admin requirement, or add the endpoint inside the existing group but with a different auth requirement.

Since the existing group has `RequireAdmin`, create a new group for the search endpoint:

```csharp
var searchGroup = app.MapGroup("/api/customers")
    .WithTags("customers")
    .RequireAuthorization(AuthPolicies.RequireUser);

searchGroup.MapGet("/search", async (string? query, int? limit, ICustomerService service, CancellationToken cancellationToken) =>
{
    var result = await service.SearchAsync(query, limit, cancellationToken);
    return ResultExtensions.ToHttpResult(result, customers => customers.Select(CustomerViewModelBuilder.ToSearch).ToArray());
}).Produces<List<CustomerSearchViewModel>>();
```

Add this before the existing `var group = app.MapGroup(...)` block. The search endpoint will be at `/api/customers/search` and requires `RequireUser` instead of `RequireAdmin`.

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build BE/WorkslipApi/Workslip.slnx`

---

## Task 9: Update EfJobRepository to Store Snapshot Fields

**Files:**
- Modify: `BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfJobRepository.cs`

- [ ] **Step 1: Update CreateAsyncCoreAsync to store snapshots**

In the `CreateAsyncCoreAsync` method, after the customer upsert and before creating the `JobReportRow`, resolve the customer data to populate snapshot fields. Replace the `JobReportRow` creation block (around line 75):

```csharp
string? snapshotName = null;
string? snapshotEmail = null;
string? snapshotPhone = null;
string? snapshotAddress = null;

if (request.Customer is not null)
{
    snapshotName = !string.IsNullOrWhiteSpace(request.Customer.Name) ? request.Customer.Name.Trim() : null;
    snapshotEmail = !string.IsNullOrWhiteSpace(request.Customer.Email) ? request.Customer.Email.Trim() : null;
    snapshotPhone = !string.IsNullOrWhiteSpace(request.Customer.Phone) ? request.Customer.Phone.Trim() : null;
    snapshotAddress = !string.IsNullOrWhiteSpace(request.Customer.Address) ? request.Customer.Address.Trim() : null;

    // If snapshot fields are empty but CustomerId is provided, fallback to master customer
    if (customerId.HasValue && (snapshotName is null && snapshotEmail is null && snapshotPhone is null && snapshotAddress is null))
    {
        var masterCustomer = await _dbContext.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId.Value && c.OrganizationId == organizationId, cancellationToken);

        if (masterCustomer is not null)
        {
            snapshotName ??= masterCustomer.Name;
            snapshotEmail ??= masterCustomer.Email;
            snapshotPhone ??= masterCustomer.Phone;
            snapshotAddress ??= masterCustomer.Address;
        }
    }
}

_dbContext.JobReports.Add(new JobReportRow
{
    Id = reportId,
    OrganizationId = organizationId,
    CustomerId = customerId,
    CustomerName = snapshotName,
    CustomerEmail = snapshotEmail,
    CustomerPhone = snapshotPhone,
    CustomerAddress = snapshotAddress,
    ReportNumber = reportNumber,
    Status = JobStatus.Draft.ToString(),
    ReportDate = ToDateTime(request.Observations?.ReportDate),
    TaskDescription = request.Observations?.TaskDescription,
    CustomerObservations = request.Observations?.CustomerObservations,
    TechnicalObservations = request.Observations?.TechnicalObservations,
    WorkKindId = workKindId,
    CustomWorkKind = request.Work?.CustomWorkKind,
    Remarks = request.Work?.Remarks,
    CreatedAt = now,
    UpdatedAt = now
});
```

- [ ] **Step 2: Update UpdateAsyncCoreAsync to store snapshots**

In the `UpdateAsyncCoreAsync` method, replace the customer upsert and entry update block (lines 277-282) with:

```csharp
var customerId = existing.CustomerId;
if (request.Customer is not null)
{
    customerId = await _customerRepository.UpsertCustomerAsync(organizationId, request.Customer, cancellationToken);
}

var entry = _dbContext.Entry(existing);
entry.Property(e => e.CustomerId).CurrentValue = customerId;

if (request.Customer is not null)
{
    entry.Property(e => e.CustomerName).CurrentValue =
        !string.IsNullOrWhiteSpace(request.Customer.Name) ? request.Customer.Name.Trim() : null;
    entry.Property(e => e.CustomerEmail).CurrentValue =
        !string.IsNullOrWhiteSpace(request.Customer.Email) ? request.Customer.Email.Trim() : null;
    entry.Property(e => e.CustomerPhone).CurrentValue =
        !string.IsNullOrWhiteSpace(request.Customer.Phone) ? request.Customer.Phone.Trim() : null;
    entry.Property(e => e.CustomerAddress).CurrentValue =
        !string.IsNullOrWhiteSpace(request.Customer.Address) ? request.Customer.Address.Trim() : null;
}
```

This keeps the existing `entry` variable and adds snapshot field updates after the CustomerId update.

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build BE/WorkslipApi/Workslip.slnx`

---

## Task 10: Update JobReportMapper to Use Snapshot Fields

**Files:**
- Modify: `BE/WorkslipApi/Workslip.Infrastructure/Mappers/JobReportMapper.cs`

- [ ] **Step 1: Update ToResponse to use snapshot fields with fallback**

In the `ToResponse` method (line 23), change the customer construction to use snapshot fields with fallback to the joined customer:

```csharp
var customer = row.CustomerRow;
var organizationName = row.OrganizationRow?.Name ?? "-";
var organizationCvr = row.OrganizationRow?.Cvr ?? "-";

var customerName = row.CustomerName ?? customer?.Name;
var customerEmail = row.CustomerEmail ?? customer?.Email;
var customerPhone = row.CustomerPhone ?? customer?.Phone;
var customerAddress = row.CustomerAddress ?? customer?.Address;
var contactPerson = customer?.ContactPerson;

var customerInfo = customerName is not null
    ? new CustomerInfo(customer?.Id, customerName, customerAddress, customerEmail, contactPerson, customerPhone)
    : null;

return new(
    row.Id, row.OrganizationId, organizationName, organizationCvr,
    customerInfo,
    row.ReportNumber, ParseStatus(row.Status), ToDateOnly(row.ReportDate),
    row.TaskDescription, row.CustomerObservations, row.TechnicalObservations,
    installationTypes, ToWorkKindResponse(row.WorkKindRow, row.CustomWorkKind),
    row.Remarks, closureFlags, links,
    row.CreatedAt, row.UpdatedAt,
    assignedUsers, worksheetEntries,
    row.IsSoftDeleted, row.DeletionScheduledAt, totalHours);
```

- [ ] **Step 2: Update ListAsyncCoreAsync to use snapshot fields**

In `EfJobRepository.cs`, the `ListAsyncCoreAsync` method (around line 130) joins with Customers to get customer data. Update the projection to use snapshot fields with fallback:

Change the select projection:
```csharp
select new
{
    r.Id,
    r.OrganizationId,
    CustId = r.CustomerId,
    CustName = r.CustomerName ?? (c != null ? c.Name : null),
    CustAddress = r.CustomerAddress ?? (c != null ? c.Address : null),
    CustEmail = r.CustomerEmail ?? (c != null ? c.Email : null),
    CustContactPerson = c != null ? c.ContactPerson : null,
    CustPhone = r.CustomerPhone ?? (c != null ? c.Phone : null),
    r.ReportNumber,
    r.Status,
    r.ReportDate,
    WorkKind = r.WorkKindRow != null ? new JobWorkKindResponse(
        r.WorkKindRow.Id,
        r.WorkKindRow.NormalizedLabel,
        r.WorkKindRow.Label,
        r.WorkKindRow.RequiresCustomWorkKind,
        r.WorkKindRow.SortOrder,
        r.CustomWorkKind) : null,
    r.CreatedAt,
    r.UpdatedAt,
    r.IsSoftDeleted,
    r.DeletionScheduledAt
}
```

Also update the list query filters (lines 138-140) to search snapshot fields:
```csharp
where query.CustomerName == null || (
    (r.CustomerName != null && r.CustomerName.Contains(query.CustomerName)) ||
    (c != null && c.Name.Contains(query.CustomerName)))
where query.CustomerEmail == null || (
    (r.CustomerEmail != null && r.CustomerEmail.Contains(query.CustomerEmail)) ||
    (c != null && c.Email != null && c.Email.Contains(query.CustomerEmail)))
where query.CustomerAddress == null || (
    (r.CustomerAddress != null && r.CustomerAddress.Contains(query.CustomerAddress)) ||
    (c != null && c.Address != null && c.Address.Contains(query.CustomerAddress)))
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build BE/WorkslipApi/Workslip.slnx`

---

## Task 11: Run Orval Codegen

**Files:**
- Regenerate: `FE/src/api/generated/customers/customers.ts`
- Regenerate: `FE/src/api/generated/models/*`

- [ ] **Step 1: Start the API**

Run: `dotnet run --project BE/WorkslipApi` (in background or separate terminal)

- [ ] **Step 2: Run orval**

Run from `FE/`: `npx orval`

This will generate a new `useGetApiCustomersSearch` hook based on the `/api/customers/search` endpoint.

---

## Task 12: Create useDebounce Hook

**Files:**
- Create: `FE/src/hooks/useDebounce.ts`

- [ ] **Step 1: Create the hook**

```typescript
import { useEffect, useState } from 'react';

export function useDebounce<T>(value: T, delayMs: number): T {
  const [debouncedValue, setDebouncedValue] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedValue(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);

  return debouncedValue;
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd FE && npx tsc --noEmit`

---

## Task 13: Create CustomerAutosuggest Component

**Files:**
- Create: `FE/src/components/forms/CustomerAutosuggest.tsx`

- [ ] **Step 1: Create the component**

```tsx
import { useCallback, useRef, useState } from 'react';
import { useDebounce } from '../../hooks/useDebounce';
import { useGetApiCustomersSearch } from '../../api/generated/customers/customers';
import type { CustomerInfo } from '../../api/generated/models';

type CustomerAutosuggestProps = {
  value: string | null;
  placeholder: string;
  customerId: string | null;
  onSelect: (customer: {
    customerId: string;
    name: string;
    email: string | null;
    phone: string | null;
    address: string | null;
  }) => void;
  onChange: (value: string | null) => void;
};

export function CustomerAutosuggest({ value, placeholder, customerId, onSelect, onChange }: CustomerAutosuggestProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [highlightIndex, setHighlightIndex] = useState(-1);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);

  const debouncedQuery = useDebounce(value ?? '', 300);
  const shouldFetch = debouncedQuery.trim().length >= 1;

  const { data: suggestions = [], isLoading } = useGetApiCustomersSearch(
    { query: debouncedQuery.trim(), limit: 10 },
    { query: { enabled: shouldFetch, queryKey: ['customers-search', debouncedQuery.trim()] } },
  );

  const handleSelect = useCallback(
    (item: (typeof suggestions)[number]) => {
      onSelect({
        customerId: item.id,
        name: item.name,
        email: item.email,
        phone: item.phone,
        address: item.address,
      });
      setIsOpen(false);
      setHighlightIndex(-1);
      inputRef.current?.blur();
    },
    [onSelect],
  );

  const handleKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (!isOpen || suggestions.length === 0) return;

    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setHighlightIndex((prev) => (prev < suggestions.length - 1 ? prev + 1 : 0));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setHighlightIndex((prev) => (prev > 0 ? prev - 1 : suggestions.length - 1));
    } else if (event.key === 'Enter' && highlightIndex >= 0) {
      event.preventDefault();
      handleSelect(suggestions[highlightIndex]);
    } else if (event.key === 'Escape') {
      setIsOpen(false);
      setHighlightIndex(-1);
    }
  };

  const handleChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const newValue = event.target.value || null;
    onChange(newValue);
    setIsOpen(true);
    setHighlightIndex(-1);
  };

  const handleFocus = () => {
    if (suggestions.length > 0) {
      setIsOpen(true);
    }
  };

  const showDropdown = isOpen && shouldFetch && (suggestions.length > 0 || isLoading);

  return (
    <div className="form-group" style={{ position: 'relative' }}>
      <input
        ref={inputRef}
        className="form-input"
        value={value ?? ''}
        onChange={handleChange}
        onFocus={handleFocus}
        onBlur={() => setTimeout(() => setIsOpen(false), 200)}
        onKeyDown={handleKeyDown}
        placeholder={placeholder}
        autoComplete="off"
        role="combobox"
        aria-expanded={showDropdown}
        aria-autocomplete="list"
      />
      {customerId && (
        <span className="customer-autosuggest-badge" style={{
          position: 'absolute',
          right: '8px',
          top: '50%',
          transform: 'translateY(-50%)',
          fontSize: '11px',
          color: '#166534',
          background: '#dcfce7',
          padding: '2px 6px',
          borderRadius: '4px',
          pointerEvents: 'none',
        }}>
          ✓ Valgt
        </span>
      )}
      {showDropdown && (
        <ul
          ref={listRef}
          role="listbox"
          className="customer-autosuggest-list"
          style={{
            position: 'absolute',
            top: '100%',
            left: 0,
            right: 0,
            zIndex: 1000,
            background: 'white',
            border: '1px solid #e2e8f0',
            borderRadius: '0 0 6px 6px',
            boxShadow: '0 4px 6px -1px rgba(0,0,0,0.1)',
            maxHeight: '240px',
            overflowY: 'auto',
            margin: 0,
            padding: 0,
            listStyle: 'none',
          }}
        >
          {isLoading && suggestions.length === 0 && (
            <li style={{ padding: '8px 12px', color: '#94a3b8', fontSize: '13px' }}>
              Søger...
            </li>
          )}
          {suggestions.map((item, index) => (
            <li
              key={item.id}
              role="option"
              aria-selected={index === highlightIndex}
              onMouseDown={() => handleSelect(item)}
              onMouseEnter={() => setHighlightIndex(index)}
              style={{
                padding: '8px 12px',
                cursor: 'pointer',
                background: index === highlightIndex ? '#f1f5f9' : 'transparent',
                borderBottom: '1px solid #f1f5f9',
                fontSize: '14px',
              }}
            >
              <div style={{ fontWeight: 500 }}>{item.name}</div>
              {(item.address || item.phone) && (
                <div style={{ fontSize: '12px', color: '#64748b', marginTop: '2px' }}>
                  {[item.address, item.phone].filter(Boolean).join(' · ')}
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd FE && npx tsc --noEmit`

---

## Task 14: Update CustomerDetailsBlock to Use Autosuggest

**Files:**
- Modify: `FE/src/features/jobs/components/JobDetailBlocks.tsx`

- [ ] **Step 1: Import the new component and update props**

Add import:
```tsx
import { CustomerAutosuggest } from '../../../components/forms/CustomerAutosuggest';
```

Update `CustomerBlockProps` to include `onCustomerSelect`:
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
  onCustomerChange: (field: keyof CustomerInfo, value: string | null) => void;
  onCustomerSelect: (customer: {
    customerId: string;
    name: string;
    email: string | null;
    phone: string | null;
    address: string | null;
  }) => void;
  onReportNumberChange: (value: string) => void;
};
```

Update the function signature to include `onCustomerSelect`:
```tsx
export function CustomerDetailsBlock({
  form,
  reportNumberReadOnly,
  assignment,
  readOnlyAssigned,
  onCustomerChange,
  onCustomerSelect,
  onReportNumberChange,
}: CustomerBlockProps) {
```

- [ ] **Step 2: Replace the name ValidatedInput with CustomerAutosuggest**

Replace the `ValidatedInput` for "Navn" (line 54):
```tsx
<CustomerAutosuggest
  value={form.customer.name}
  placeholder="Kundens navn"
  customerId={form.customer.customerId}
  onSelect={onCustomerSelect}
  onChange={(value) => onCustomerChange('name', value)}
/>
```

Note: `onCustomerSelect` is a new prop that sets all customer fields at once. We'll add it to the props type and parent hooks in Tasks 15-17.

- [ ] **Step 3: Add hint text below customer fields**

After the customer fields div (after the phone/contactPerson row), add:
```tsx
<div className="form-hint" style={{ fontSize: '12px', color: '#94a3b8', marginTop: '4px' }}>
  Ændringer her gælder kun denne sag og opdaterer ikke kundekartoteket.
</div>
```

- [ ] **Step 4: Verify it compiles**

Run: `cd FE && npx tsc --noEmit`

---

## Task 15: Update useJobCreate to Handle Customer Selection

**Files:**
- Modify: `FE/src/features/jobs/hooks/useJobCreate.ts`

- [ ] **Step 1: Add selectCustomer function**

After the `updateCustomer` function, add:

```tsx
const selectCustomer = (customer: {
  customerId: string;
  name: string;
  email: string | null;
  phone: string | null;
  address: string | null;
}) => {
  setForm((prev) => ({
    ...prev,
    customer: {
      customerId: customer.customerId,
      name: customer.name,
      email: customer.email,
      phone: customer.phone,
      address: customer.address,
      contactPerson: prev.customer.contactPerson,
    },
  }));
};
```

- [ ] **Step 2: Update save function to include customerId**

In the `save` function, change `customerId: null` to `customerId: form.customer.customerId`:

```tsx
const request: CreateJobRequest = {
  customer: {
    customerId: form.customer.customerId,
    name: form.customer.name?.trim() || null,
    address: form.customer.address?.trim() || null,
    email: form.customer.email?.trim() || null,
    contactPerson: form.customer.contactPerson?.trim() || null,
    phone: form.customer.phone?.trim() || null,
  },
  // ... rest
};
```

- [ ] **Step 3: Expose selectCustomer from the hook**

Add `selectCustomer` to the return object.

- [ ] **Step 4: Verify it compiles**

Run: `cd FE && npx tsc --noEmit`

---

## Task 16: Update useJobDetails to Handle Customer Selection

**Files:**
- Modify: `FE/src/features/jobs/hooks/useJobDetails.ts`

- [ ] **Step 1: Add selectCustomer function**

After the `updateCustomer` function, add:

```tsx
const selectCustomer = (customer: {
  customerId: string;
  name: string;
  email: string | null;
  phone: string | null;
  address: string | null;
}) => {
  updateDraft({
    ...form,
    customer: {
      customerId: customer.customerId,
      name: customer.name,
      email: customer.email,
      phone: customer.phone,
      address: customer.address,
      contactPerson: form.customer.contactPerson,
    },
  });
};
```

- [ ] **Step 2: Expose selectCustomer from the hook**

Add `selectCustomer` to the return object.

- [ ] **Step 3: Verify it compiles**

Run: `cd FE && npx tsc --noEmit`

---

## Task 17: Update CreateOverviewStep and JobOverviewStep

**Files:**
- Modify: `FE/src/features/jobs/components/steps/CreateOverviewStep.tsx`
- Modify: `FE/src/features/jobs/components/steps/JobOverviewStep.tsx`

- [ ] **Step 1: Pass selectCustomer to CustomerDetailsBlock**

In `CreateOverviewStep.tsx`, add a new prop to `CustomerDetailsBlock`:

```tsx
<CustomerDetailsBlock
  form={create.form}
  onCustomerChange={create.updateCustomer}
  onCustomerSelect={create.selectCustomer}
  onReportNumberChange={create.updateReportNumber}
  assignment={{...}}
/>
```

In `JobOverviewStep.tsx`, do the same:
```tsx
<CustomerDetailsBlock
  form={form}
  onCustomerChange={updateCustomer}
  onCustomerSelect={selectCustomer}
  onReportNumberChange={updateReportNumber}
  assignment={{...}}
/>
```

- [ ] **Step 2: Update CustomerDetailsBlock props**

In `JobDetailBlocks.tsx`, add `onCustomerSelect` to the props type:

```tsx
type CustomerBlockProps = {
  form: { customer: CustomerInfo; reportNumber: string };
  reportNumberReadOnly?: boolean;
  assignment?: { ... };
  readOnlyAssigned?: { ... };
  onCustomerChange: (field: keyof CustomerInfo, value: string | null) => void;
  onCustomerSelect?: (customer: {
    customerId: string;
    name: string;
    email: string | null;
    phone: string | null;
    address: string | null;
  }) => void;
  onReportNumberChange: (value: string) => void;
};
```

Update the autosuggest's `onSelect` to use `onCustomerSelect` if provided, otherwise fall back to individual field updates.

- [ ] **Step 3: Verify it compiles**

Run: `cd FE && npx tsc --noEmit`

---

## Task 18: Verify End-to-End

- [ ] **Step 1: Start the API and frontend**

Run the API and frontend dev servers.

- [ ] **Step 2: Test customer search endpoint**

Navigate to the job create page. Type a customer name in the name field. Verify suggestions appear.

- [ ] **Step 3: Test customer selection**

Click a suggestion. Verify all customer fields are populated. Verify the "Valgt" badge appears.

- [ ] **Step 4: Test job creation**

Fill in required fields and save. Verify the job is created with both `CustomerId` and snapshot fields populated.

- [ ] **Step 5: Test job edit**

Open the created job. Verify customer fields show the snapshot data. Edit the address. Save. Verify the customer master record is unchanged.

- [ ] **Step 6: Test PDF generation**

Generate a PDF for the job. Verify it shows the snapshot customer data.

- [ ] **Step 7: Verify legacy jobs**

Open an existing job (without snapshot fields). Verify it still shows customer data through the fallback mechanism.
