# Control Points Page (Kontrolpunkter)

## Problem

After selecting categories (anlægstyper) in step 1, the technician must document process and final control points for each selected category. There is currently no UI for this — only the backend domain model and reference data exist.

## Goals

- Add a new step "Kontrolpunkter" to the job edit flow
- Show control points grouped by selected category in collapsible sections
- Support checkbox selection per control point + category-level "Ikke relevant" toggle
- Persist selections via existing backend contract
- Block submit on missing required control points

## Non-Goals

- Client-defined taxonomy trees
- AI/OCR
- PDF rendering

## Flow Change

Insert step 2 between Kategorier and Bilag, shifting Bilag to step 3:

```
JOB_STEPS = [
  { icon: Building2, label: 'Sagsdetaljer' },    // step 0
  { icon: FileText, label: 'Kategorier' },        // step 1
  { icon: ClipboardList, label: 'Kontrolpunkter' },// step 2 (NEW)
  { icon: MessageSquare, label: 'Bilag' },         // step 3
]
```

Step 2 is only accessible when `form.work.categoryIds.length > 0` (at least one category selected).

## Data Source

All control point data comes from the existing `referenceData` endpoint which returns:

```
InstallationTypeDefinition
  └── Categories[]
       └── ControlPoints[]
            ├── id
            ├── name
            ├── description
            ├── sortOrder
            └── isRequired
```

The FE types are missing `name` and `description` on `DefinitionControlPointResponse`-mapped types — these must be added.

## Component: `ControlPointsStep`

New file: `features/jobs/components/steps/ControlPointsStep.tsx`

### Props

```ts
type ControlPointsStepProps = {
  form: JobForm;
  referenceData: ReferenceData | null;
  onControlPointToggle: (categoryId: string, controlPointId: string) => void;
  onCategoryIrrelevantToggle: (categoryId: string) => void;
};
```

### Layout

```
┌─ detail-section ────────────────────────────────┐
│ 📋 Kontrolpunkter                                │
│                                                  │
│ ┌─ CollapsibleSection (closed by default) ─────┐ │
│ │ 🔽 Gas                                        │ │
│ │ ───────────────────────────────────────────── │ │
│ │ ☐ Kontrolpunkt A (isRequired: *)              │ │
│ │ ☑ Kontrolpunkt B                              │ │
│ │ ☐ Kontrolpunkt C                              │ │
│ │ ───────────────────────────────────────────── │ │
│ │ ☐ Ikke relevant                               │ │
│ └───────────────────────────────────────────────┘ │
│                                                  │
│ ┌─ CollapsibleSection (closed by default) ─────┐ │
│ │ 🔽 Vand                                       │ │
│ │ ───────────────────────────────────────────── │ │
│ │ ☐ Kontrolpunkt D (isRequired: *)              │ │
│ │ ☑ Ikke relevant ← disabled below             │ │
│ │ ───────────────────────────────────────────── │ │
│ │ ☐ Kontrolpunkt E (disabled)                   │ │
│ │ ☐ Kontrolpunkt F (disabled)                   │ │
│ └───────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────┘
```

### Interaction rules

- Each `CollapsibleSection` is **closed by default** (`defaultOpen={false}`)
- Control points listed in `sortOrder`, each with a checkbox + label + optional description text
- **"Ikke relevant" checkbox** at the bottom of each category's control point list
- Checking "Ikke relevant" → all other checkboxes in that category get disabled + unchecked
- Checking any other control point while "Ikke relevant" is on → "Ikke relevant" unchecks automatically
- Required control points (`isRequired: true`) show a visual indicator (e.g. `*` or `(obligatorisk)`)

## Form State Changes

### `types.ts`

Add `controlPointSelections` to `JobWorkForm`:

```ts
export type JobWorkForm = {
  categoryIds: string[];
  workKind: string;
  customWorkKind: string;
  controlPointSelections: Record<string, {  // key = categoryId
    isIrrelevant: boolean;
    checkedControlPointIds: string[];
  }>;
};
```

Fix `ReferenceCategory` types to include `name`/`description`:

```ts
categories: Array<{
  id: string;
  name: string;
  description?: string;
  controlPoints: Array<{
    id: string;
    name: string;
    description?: string;
    sortOrder: number | string;
    isRequired: boolean;
  }>;
}>;
```

### Integration

| File | Change |
|------|--------|
**Important**: This step is only added to the edit flow (JobDetail), NOT the create flow (JobCreate). The create flow remains a single-page form ending on job creation.

| File | Change |
|------|--------|
| `routes/index.tsx` | No change |
| `components/steps/JobStepNavigation.tsx` | Add step 2 "Kontrolpunkter" to `JOB_STEPS` |
| `components/JobDetails.tsx` | Render `ControlPointsStep` at `currentStep === 2`; shift `isLastStep` to `=== 3` |
| `types.ts` | Add `controlPointSelections` + fix control point type fields |
| `utils.ts` | Add helpers; update `toWorkRequest`; add `sameControlPoints()`; add `getControlPointsValidationMessage()` |
| `hooks/useJobDetails.ts` | Add `toggleControlPoint()`, `toggleCategoryIrrelevant()` updaters |
| `components/steps/ControlPointsStep.tsx` | **New file** — the page component |

### Validation

Block submit if any selected category has required control points that are not checked AND the category is not marked irrelevant.

```ts
function getControlPointsValidationMessage(
  form: JobForm,
  referenceData: ReferenceData | null,
): string | null {
  // For each selected category:
  //   if not isIrrelevant:
  //     find required control points not in checkedControlPointIds
  // Return first missing category name or null
}
```

## Edge Cases

- **No categories selected**: Step 2 is skipped/disabled (user can't navigate to it)
- **All control points required**: All must be checked or category must be irrelevant before proceeding
- **Category irrelevant after checking control points**: All checked items clear when irrelevant is toggled on
- **Unchecking irrelevant**: Previously checked items remain checked (we don't restore history), user re-selects
- **Edit mode (JobDetail)**: Load existing selections from `job.work.installationTypes[].categories[].isIrrelevant` and `job.work.installationTypes[].categories[].controlPoints[].id` for checked state
