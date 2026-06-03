# Control Points Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Kontrolpunkter" step (step 2 of 4) to the job edit flow, showing control points grouped by category in collapsible sections with checkbox selection and category-level "Ikke relevant" toggle.

**Architecture:** New `ControlPointsStep` component renders per-category collapsible sections using the existing `CollapsibleSection`. State stored in `JobWorkForm.controlPointSelections` (a map keyed by category ID). Data sourced from existing `referenceData` endpoint. Only added to the edit flow (JobDetail) — JobCreate remains unchanged.

**Tech Stack:** React 19, TypeScript, react-router-dom 7, @tanstack/react-query, lucide-react

---

### Task 1: Update types.ts — add control point form state

**Files:**
- Modify: `FE/src/features/jobs/types.ts`

- [ ] **Add `controlPointSelections` to `JobWorkForm` and fix reference data types**

```ts
export type JobWorkForm = {
  categoryIds: string[];
  workKind: string;
  customWorkKind: string;
  controlPointSelections: Record<string, {
    isIrrelevant: boolean;
    checkedControlPointIds: string[];
  }>;
};
```

Fix `ReferenceCategory` to include missing `name`/`description` fields that the BE actually returns:

```ts
export type ReferenceCategory = {
  id: string;
  name: string;
  sortOrder: number | string;
  categories: Array<{
    id: string;
    name: string;
    description?: string;
    sortOrder: number | string;
    controlPoints: Array<{
      id: string;
      name: string;
      description?: string;
      sortOrder: number | string;
      isRequired: boolean;
    }>;
  }>;
};
```

- [ ] **Commit**

```bash
git add FE/src/features/jobs/types.ts
git commit -m "feat(rbj-52): add control point selections to form types"
```

---

### Task 2: Update utils.ts — form mapping and validation

**Files:**
- Modify: `FE/src/features/jobs/utils.ts`

- [ ] **Add `buildControlPointSelections` helper to extract selections from job response**

This reads the job's existing control point state when editing.

```ts
import type {
  InstallationTypeCategoryResponse,
} from '../../api/generated/models/installationTypeCategoryResponse';

export function buildControlPointSelections(
  installationTypes: Array<{ categories: Array<{ id: string; isIrrelevant?: boolean | null; controlPoints: Array<{ id: string; isChecked: boolean }> }> }>,
): Record<string, { isIrrelevant: boolean; checkedControlPointIds: string[] }> {
  const selections: Record<string, { isIrrelevant: boolean; checkedControlPointIds: string[] }> = {};

  for (const instType of installationTypes) {
    for (const cat of instType.categories) {
      selections[cat.id] = {
        isIrrelevant: cat.isIrrelevant ?? false,
        checkedControlPointIds: cat.controlPoints
          .filter((cp) => cp.isChecked)
          .map((cp) => cp.id),
      };
    }
  }

  return selections;
}
```

- [ ] **Update `toForm` to populate `controlPointSelections`**

```ts
export function toForm(job: JobReportSummaryViewModel): JobForm {
  return {
    customer: {
      customerId: job.customer.customerId ?? null,
      name: job.customer.name ?? null,
      address: job.customer.address ?? null,
      email: job.customer.email ?? null,
      contactPerson: job.customer.contactPerson ?? null,
      phone: job.customer.phone ?? null,
    },
    reportNumber: job.reportNumber ?? '',
    taskDescription: job.observations.taskDescription ?? '',
    customerObservations: job.observations.customerObservations ?? '',
    work: {
      categoryIds: job.work.installationTypes.map((installationType) => installationType.id),
      workKind: job.work.workKind?.normalizedLabel ?? '',
      customWorkKind: job.work.workKind?.customWorkKind ?? '',
      controlPointSelections: buildControlPointSelections(job.work.installationTypes),
    },
  };
}
```

- [ ] **Update `toWorkRequest` to use `controlPointSelections` instead of hardcoding all control points**

```diff
 export function toWorkRequest(
   form: JobForm,
   referenceData: ReferenceData | null,
 ): CreateJobWorkRequest {
   const selectedCategories = referenceData?.installationTypes
     .filter((category) => form.work.categoryIds.includes(category.id)) ?? [];

   return {
     installationTypes: selectedCategories.map((category) => ({
       id: category.id,
-      categories: category.categories.map((subcategory) => ({
-        id: subcategory.id,
-        controlPoints: subcategory.controlPoints.map((controlPoint) => ({
-          id: controlPoint.id,
-          sortOrder: controlPoint.sortOrder,
-          isRequired: controlPoint.isRequired,
-        })),
-        isIrrelevant: false,
-      })),
+      categories: category.categories.map((subcategory) => {
+        const selection = form.work.controlPointSelections[subcategory.id];
+        const isIrrelevant = selection?.isIrrelevant ?? false;
+        const checkedControlPointIds = selection?.checkedControlPointIds ?? [];
+
+        return {
+          id: subcategory.id,
+          controlPoints: isIrrelevant
+            ? []
+            : subcategory.controlPoints
+                .filter((cp) => checkedControlPointIds.includes(cp.id))
+                .map((cp) => ({
+                  id: cp.id,
+                  sortOrder: cp.sortOrder,
+                  isRequired: cp.isRequired,
+                })),
+          isIrrelevant,
+        };
+      }),
     })),
     workKind: form.work.workKind || null,
     customWorkKind: form.work.customWorkKind.trim() || null,
     closureFlags: null,
     remarks: null,
   };
 }
```

- [ ] **Add `sameControlPoints` comparison and validation message**

```ts
export function sameControlPoints(left: JobForm, right: JobForm) {
  return JSON.stringify(left.work.controlPointSelections) === JSON.stringify(right.work.controlPointSelections);
}

export function getControlPointsValidationMessage(
  form: JobForm,
  referenceData: ReferenceData | null,
): string | null {
  const selectedTypes = referenceData?.installationTypes
    .filter((t) => form.work.categoryIds.includes(t.id)) ?? [];

  for (const instType of selectedTypes) {
    for (const cat of instType.categories) {
      const selection = form.work.controlPointSelections[cat.id];
      if (selection?.isIrrelevant) continue;

      const requiredPoints = cat.controlPoints.filter((cp) => cp.isRequired);
      const checked = selection?.checkedControlPointIds ?? [];

      for (const rp of requiredPoints) {
        if (!checked.includes(rp.id)) {
          return `Manglende obligatorisk kontrolpunkt i "${cat.name}": "${rp.name}"`;
        }
      }
    }
  }

  return null;
}
```

- [ ] **Commit**

```bash
git add FE/src/features/jobs/utils.ts
git commit -m "feat(rbj-52): update form mapping with control point selections"
```

---

### Task 3: Update emptyForm in utils.ts

- [ ] **Add empty `controlPointSelections` to `emptyForm`**

```diff
 export const emptyForm: JobForm = {
   customer: { ...emptyCustomer },
   reportNumber: '',
   taskDescription: '',
   customerObservations: '',
   work: {
     categoryIds: [],
     workKind: '',
     customWorkKind: '',
+    controlPointSelections: {},
   },
 };
```

- [ ] **Commit**

```bash
git add FE/src/features/jobs/utils.ts
git commit -m "feat(rbj-52): add empty control point selections to default form"
```

---

### Task 4: Create ControlPointsStep component

**Files:**
- Create: `FE/src/features/jobs/components/steps/ControlPointsStep.tsx`

- [ ] **Create the ControlPointsStep component**

```tsx
import { ClipboardList } from 'lucide-react';
import { CollapsibleSection } from '../../../components/forms/CollapsibleSection';
import type { JobForm, ReferenceData } from '../../types';

type ControlPointsStepProps = {
  form: JobForm;
  referenceData: ReferenceData | null;
  onControlPointToggle: (categoryId: string, controlPointId: string) => void;
  onCategoryIrrelevantToggle: (categoryId: string) => void;
};

export function ControlPointsStep({
  form,
  referenceData,
  onControlPointToggle,
  onCategoryIrrelevantToggle,
}: ControlPointsStepProps) {
  const selectedTypes = (referenceData?.installationTypes ?? [])
    .filter((t) => form.work.categoryIds.includes(t.id));

  if (form.work.categoryIds.length === 0) {
    return (
      <section className="detail-section">
        <div className="section-header-row">
          <ClipboardList size={18} />
          <h3>Kontrolpunkter</h3>
        </div>
        <p className="empty-state-text">Vælg mindst én kategori for at se kontrolpunkter.</p>
      </section>
    );
  }

  return (
    <section className="detail-section">
      <div className="section-header-row">
        <ClipboardList size={18} />
        <h3>Kontrolpunkter</h3>
      </div>

      {selectedTypes.map((instType) =>
        instType.categories.map((cat) => {
          const selection = form.work.controlPointSelections[cat.id] ?? {
            isIrrelevant: false,
            checkedControlPointIds: [],
          };

          return (
            <CollapsibleSection
              key={cat.id}
              icon={<ClipboardList size={18} />}
              title={cat.name}
              defaultOpen={false}
            >
              <div className="control-points-list">
                {cat.controlPoints
                  .sort((a, b) => Number(a.sortOrder) - Number(b.sortOrder))
                  .map((cp) => {
                    const isChecked = selection.checkedControlPointIds.includes(cp.id);
                    const disabled = selection.isIrrelevant;

                    return (
                      <label
                        key={cp.id}
                        className={`control-point-item ${disabled ? 'disabled' : ''}`}
                      >
                        <input
                          type="checkbox"
                          checked={isChecked}
                          disabled={disabled}
                          onChange={() => onControlPointToggle(cat.id, cp.id)}
                        />
                        <span className="control-point-label">
                          {cp.name}
                          {cp.isRequired && <span className="required-marker"> *</span>}
                        </span>
                        {cp.description && (
                          <span className="control-point-description">{cp.description}</span>
                        )}
                      </label>
                    );
                  })}

                <label className="control-point-item control-point-irrelevant">
                  <input
                    type="checkbox"
                    checked={selection.isIrrelevant}
                    onChange={() => onCategoryIrrelevantToggle(cat.id)}
                  />
                  <span className="control-point-label">Ikke relevant</span>
                </label>
              </div>
            </CollapsibleSection>
          );
        }),
      )}
    </section>
  );
}
```

- [ ] **Commit**

```bash
git add FE/src/features/jobs/components/steps/ControlPointsStep.tsx
git commit -m "feat(rbj-52): create ControlPointsStep component"
```

---

### Task 5: Add CSS for control points layout

**Files:**
- Modify: `FE/src/App.css`

- [ ] **Add control points styles to App.css**

Search for the `collapsible-section-content` block in App.css and add these styles after it:

```css
.control-points-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.control-point-item {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem;
  border-radius: 6px;
  cursor: pointer;
  transition: background-color 0.15s;
}

.control-point-item:hover {
  background-color: rgba(255, 255, 255, 0.05);
}

.control-point-item.disabled {
  opacity: 0.4;
  pointer-events: none;
}

.control-point-item input[type="checkbox"] {
  width: 1.125rem;
  height: 1.125rem;
  accent-color: var(--accent);
  cursor: pointer;
}

.control-point-label {
  font-size: 0.9rem;
  font-weight: 500;
}

.required-marker {
  color: #e74c3c;
  margin-left: 0.125rem;
}

.control-point-description {
  width: 100%;
  font-size: 0.8rem;
  color: var(--text-secondary, #888);
  margin-left: 1.625rem;
}

.control-point-irrelevant {
  border-top: 1px solid rgba(255, 255, 255, 0.1);
  margin-top: 0.25rem;
  padding-top: 0.75rem;
}
```

- [ ] **Commit**

```bash
git add FE/src/App.css
git commit -m "feat(rbj-52): add control points CSS styles"
```

---

### Task 6: Update useJobDetails hook — add control point updaters

**Files:**
- Modify: `FE/src/features/jobs/hooks/useJobDetails.ts`

- [ ] **Add `toggleControlPoint` and `toggleCategoryIrrelevant` functions**

Add these before the `return` statement:

```ts
const toggleControlPoint = (categoryId: string, controlPointId: string) => {
  const current = form.work.controlPointSelections[categoryId] ?? {
    isIrrelevant: false,
    checkedControlPointIds: [],
  };

  const isChecked = current.checkedControlPointIds.includes(controlPointId);

  updateDraft({
    ...form,
    work: {
      ...form.work,
      controlPointSelections: {
        ...form.work.controlPointSelections,
        [categoryId]: {
          isIrrelevant: false,
          checkedControlPointIds: isChecked
            ? current.checkedControlPointIds.filter((id) => id !== controlPointId)
            : [...current.checkedControlPointIds, controlPointId],
        },
      },
    },
  });
};

const toggleCategoryIrrelevant = (categoryId: string) => {
  const current = form.work.controlPointSelections[categoryId] ?? {
    isIrrelevant: false,
    checkedControlPointIds: [],
  };

  updateDraft({
    ...form,
    work: {
      ...form.work,
      controlPointSelections: {
        ...form.work.controlPointSelections,
        [categoryId]: {
          isIrrelevant: !current.isIrrelevant,
          checkedControlPointIds: current.isIrrelevant ? current.checkedControlPointIds : [],
        },
      },
    },
  });
};
```

Add to the return object:

```ts
toggleControlPoint,
toggleCategoryIrrelevant,
```

- [ ] **Commit**

```bash
git add FE/src/features/jobs/hooks/useJobDetails.ts
git commit -m "feat(rbj-52): add control point toggle handlers to useJobDetails"
```

---

### Task 7: Update JobStepNavigation — add step 2

**Files:**
- Modify: `FE/src/features/jobs/components/steps/JobStepNavigation.tsx`

- [ ] **Add "Kontrolpunkter" step to JOB_STEPS**

```diff
 export const JOB_STEPS = [
   { icon: Building2, label: 'Sagsdetaljer' },
   { icon: FileText, label: 'Kategorier' },
+  { icon: ClipboardList, label: 'Kontrolpunkter' },
   { icon: MessageSquare, label: 'Bilag' },
 ] as const;
```

Add the import:

```diff
-import { Building2, CheckCircle2, ChevronLeft, ChevronRight, FileText, MessageSquare } from 'lucide-react';
+import { Building2, CheckCircle2, ChevronLeft, ChevronRight, ClipboardList, FileText, MessageSquare } from 'lucide-react';
```

- [ ] **Commit**

```bash
git add FE/src/features/jobs/components/steps/JobStepNavigation.tsx
git commit -m "feat(rbj-52): add Kontrolpunkter step to navigation"
```

---

### Task 8: Update JobDetails — render ControlPointsStep

**Files:**
- Modify: `FE/src/features/jobs/components/JobDetails.tsx`

- [ ] **Import and render ControlPointsStep**

```diff
 import { JobAttachmentsStep } from './steps/JobAttachmentsStep';
 import { JobOverviewStep } from './steps/JobOverviewStep';
+import { ControlPointsStep } from './steps/ControlPointsStep';
 import { JOB_STEPS, StepIndicators, StepNavigation } from './steps/JobStepNavigation';
 import { WorkCategoryStep } from './steps/WorkCategoryStep';
```

Add rendering at `currentStep === 2`:

```tsx
      {details.currentStep === 1 && (
        <WorkCategoryStep
          form={details.form}
          referenceData={details.referenceData}
          isLoading={details.isLoadingReferenceData}
          onCategoriesChange={details.updateWorkCategories}
          onWorkKindChange={details.updateWorkKind}
          onCustomWorkKindChange={details.updateCustomWorkKind}
        />
      )}

+      {details.currentStep === 2 && (
+        <ControlPointsStep
+          form={details.form}
+          referenceData={details.referenceData}
+          onControlPointToggle={details.toggleControlPoint}
+          onCategoryIrrelevantToggle={details.toggleCategoryIrrelevant}
+        />
+      )}

-      {details.currentStep === 2 && (
+      {details.currentStep === 3 && (
         <JobAttachmentsStep />
       )}
```

Update `isLastStep`:

```diff
-  const isLastStep = details.currentStep === JOB_STEPS.length - 1;
+  const isLastStep = details.currentStep === 3;
```

- [ ] **Commit**

```bash
git add FE/src/features/jobs/components/JobDetails.tsx
git commit -m "feat(rbj-52): render ControlPointsStep in edit flow"
```

---

### Self-review checklist

1. **Spec coverage:** Steps, collapsible sections (closed by default), checkboxes, "Ikke relevant" toggle, validation, edit-only — all covered.
2. **Placeholder scan:** No TBD, TODO, or vague instructions.
3. **Type consistency:** `controlPointSelections` keyed by category ID matches how `toWorkRequest` looks up selections. `toForm` reads `isChecked`/`isIrrelevant` from response.
4. **Scope:** Only edit flow (JobDetail), no JobCreate changes.
