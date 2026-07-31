# WOR-240 Post-Approval Navigation Modal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After an admin approves or rejects a job, show a modal asking whether to go to the job list or to the case just acted on, instead of auto-redirecting to the list.

**Architecture:** Frontend-only change in `src/FE`. Generalize the existing inline `UndoRejectionSuccessDialog` in `CompletedJobReport.tsx` into one `ActionSuccessDialog` handling `approve` / `reject` / `undo-reject`, and replace the automatic `navigate(from)` + success toast for approve/reject with the dialog.

**Tech Stack:** React 19, react-router-dom 7, TanStack Query 5, vitest 4 + @testing-library/react (jsdom).

---

## Task 1: Add failing unit test for the post-approval/reject modal

**Files:**
- Create: `src/FE/src/features/jobs/routes/CompletedJobReport.navigation.test.tsx`
- Test: `src/FE/src/features/jobs/routes/CompletedJobReport.navigation.test.tsx`

- [ ] **Step 1: Create the test file**

Create `src/FE/src/features/jobs/routes/CompletedJobReport.navigation.test.tsx` with exactly this content:

```tsx
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { JobStatus } from '../../../api/generated/models';
import type { JobReportSummaryViewModel } from '../../../api/generated/models';
import { CompletedJobReport } from './CompletedJobReport';

const { navigateMock } = vi.hoisted(() => ({ navigateMock: vi.fn() }));

const state = vi.hoisted(() => {
  const job: JobReportSummaryViewModel = {
    id: 'job-1',
    organizationId: 'org-1',
    reportNumber: '123',
    status: JobStatus.InReview,
    customerId: 'cust-1',
    customerSnapshot: {
      name: 'ACME A/S',
      address: 'Vej 1',
      contactPerson: 'Bo',
      phone: '12345678',
      email: 'bo@acme.dk',
    },
    destinationAddress: 'Vej 2',
    destinationZipCode: '1000',
    destinationCity: 'København',
    work: {
      workKind: {
        id: 'wk-1',
        normalizedLabel: 'service',
        label: 'Service',
        requiresCustomWorkKind: false,
        sortOrder: 1,
        customWorkKind: null,
      },
      installationTypes: [],
      closureFlags: [],
      remarks: null,
    },
    observations: {
      taskDescription: 'Opgave',
      customerObservations: null,
      technicalObservations: null,
    },
    links: [],
    assignedUsers: [],
    worksheets: [],
    totalHours: 8,
    totalOutlay: null,
    softDeleted: false,
    jobType: 'KLS',
    rejectionNote: null,
  };
  return { job };
});

const mutation = vi.hoisted(() => ({
  isPending: false,
  mutateAsync: vi.fn(),
}));

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return {
    ...actual,
    useNavigate: () => navigateMock,
    useParams: () => ({ id: 'job-1' }),
    useLocation: () => ({ state: { from: '/app' } }),
  };
});

vi.mock('../../../api/generated/jobs/jobs', () => ({
  getGetApiJobsIdQueryKey: (id: string) => ['jobs', id],
  getGetApiJobsQueryKey: () => ['jobs'],
  usePostApiJobsIdStatus: () => mutation,
}));

vi.mock('../../../lib/toast', () => ({
  notify: { success: vi.fn(), error: vi.fn() },
}));

vi.mock('../../../providers/permissions/usePermissions', () => ({
  useIsAdmin: () => true,
}));

vi.mock('../../../providers/useAuth', () => ({
  useAuth: () => ({ user: { id: 'user-1' } }),
}));

vi.mock('../../../hooks/useMediaQuery', () => ({
  useMediaQuery: () => true,
}));

vi.mock('../../../hooks/useScrollRestore', () => ({
  useScrollRestore: vi.fn(),
}));

vi.mock('../hooks/useJobDetails', () => ({
  useJobDetailsState: () => ({
    job: state.job,
    form: { reportNumber: '123' },
    referenceData: null,
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
    discardChanges: vi.fn(),
    saveAllChanges: vi.fn(),
    hasUnsavedChanges: false,
    saveStatus: 'idle',
  }),
}));

vi.mock('../utils/markJobSeen', () => ({
  markJobAsSeen: vi.fn(),
}));

vi.mock('../components/ConfirmActionDialog', () => ({
  ConfirmActionDialog: ({
    action,
    onConfirm,
  }: {
    action: 'approve' | 'reject' | 'undo-reject';
    onConfirm: (note?: string) => void;
  }) => (
    <button type="button" onClick={() => onConfirm()}>
      {action === 'approve'
        ? 'Bekræft godkendelse'
        : action === 'reject'
          ? 'Bekræft afvisning'
          : 'Bekræft fortrydelse'}
    </button>
  ),
}));

vi.mock('../components/JobHistoryDrawer', () => ({ JobHistoryDrawer: () => null }));
vi.mock('../components/CompletedJobEditForm', () => ({ CompletedJobEditForm: () => null }));
vi.mock('../components/DetailGrid', () => ({ DetailGrid: () => null }));
vi.mock('../components/AssignedUsers', () => ({ AssignedUsers: () => null }));
vi.mock('../components/LinkedJobs', () => ({ LinkedJobs: () => null }));
vi.mock('../components/WorksheetDetailList', () => ({ WorksheetDetailList: () => null }));
vi.mock('../components/ControlPointOverview', () => ({
  getSelectedControlPoints: () => [],
  getIrrelevantCategories: () => [],
  ControlPointOverview: () => null,
}));
vi.mock('../../../components/forms/CollapsibleSection', () => ({ CollapsibleSection: () => null }));
vi.mock('../../../components/forms/NavigationGuard', () => ({ NavigationGuard: () => null }));
vi.mock('../../../components/StatusBanner', () => ({ StatusBanner: () => null }));
vi.mock('../../../components/ErrorState', () => ({ ErrorState: () => null }));

function renderCompletedJobReport() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/app/completed/job-1']}>
        <Routes>
          <Route path="/app/completed/:id" element={<CompletedJobReport />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

async function approveJob() {
  fireEvent.click(screen.getByRole('button', { name: 'Godkend' }));
  fireEvent.click(screen.getByRole('button', { name: 'Bekræft godkendelse' }));
  await screen.findByRole('heading', { name: 'Sagen er godkendt' });
}

async function rejectJob() {
  fireEvent.click(screen.getByRole('button', { name: 'Afvis' }));
  fireEvent.click(screen.getByRole('button', { name: 'Bekræft afvisning' }));
  await screen.findByRole('heading', { name: 'Sagen er afvist' });
}

describe('CompletedJobReport post-approval navigation', () => {
  beforeEach(() => {
    mutation.mutateAsync.mockResolvedValue(state.job);
  });

  afterEach(() => cleanup());

  it('shows the navigation modal after approving without navigating yet', async () => {
    renderCompletedJobReport();

    await approveJob();

    expect(screen.getByRole('button', { name: 'Til sagslisten' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Til sagen' })).toBeInTheDocument();
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('navigates to the job list from the modal after approving', async () => {
    renderCompletedJobReport();

    await approveJob();
    fireEvent.click(screen.getByRole('button', { name: 'Til sagslisten' }));

    expect(navigateMock).toHaveBeenCalledWith('/app', expect.objectContaining({ replace: true }));
  });

  it('navigates to the case from the modal after approving', async () => {
    renderCompletedJobReport();

    await approveJob();
    fireEvent.click(screen.getByRole('button', { name: 'Til sagen' }));

    expect(navigateMock).toHaveBeenCalledWith(
      '/app/completed/job-1',
      expect.objectContaining({ replace: true }),
    );
  });

  it('shows the navigation modal after rejecting', async () => {
    renderCompletedJobReport();

    await rejectJob();

    expect(screen.getByRole('button', { name: 'Til sagslisten' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Til sagen' })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the new test and verify it fails**

From `src/FE`:

```
npx vitest run src/features/jobs/routes/CompletedJobReport.navigation.test.tsx
```

Expected: FAIL. The current component calls `navigate(from)` after approval instead of rendering the modal, so `findByRole('heading', { name: 'Sagen er godkendt' })` times out and the first test fails with "Unable to find an element with role heading and name /Sagen er godkendt/".

- [ ] **Step 3: Commit**

```bash
git add src/FE/src/features/jobs/routes/CompletedJobReport.navigation.test.tsx
git commit -m "wor-240: failing test for post-approval navigation modal"
```

---

## Task 2: Implement ActionSuccessDialog in CompletedJobReport

**Files:**
- Modify: `src/FE/src/features/jobs/routes/CompletedJobReport.tsx`

All edits below are to `src/FE/src/features/jobs/routes/CompletedJobReport.tsx`.

- [ ] **Step 1: Replace the `undoRejectionCompleted` state with `completedAction`**

Find (line 56):

```tsx
  const [undoRejectionCompleted, setUndoRejectionCompleted] = useState(false);
```

Replace with:

```tsx
  const [completedAction, setCompletedAction] = useState<'approve' | 'reject' | 'undo-reject' | null>(null);
```

- [ ] **Step 2: Set `completedAction` instead of toasting and navigating on success**

Find inside `executeConfirmAction` (the success block):

```tsx
      queryClient.setQueryData(getGetApiJobsIdQueryKey(job.id), updatedJob);
      await queryClient.invalidateQueries({ queryKey: getGetApiJobsQueryKey() });
      const message = confirmAction === 'undo-reject'
        ? `Sagen ${details.form.reportNumber} er sendt til gennemgang igen`
        : confirmAction === 'approve'
          ? `${details.form.reportNumber} er godkendt`
          : `${details.form.reportNumber} er afvist`;
      setConfirmAction(null);

      if (confirmAction === 'undo-reject') {
        setUndoRejectionCompleted(true);
        return;
      }

      notify.success(message);
      navigate(from);
```

Replace with:

```tsx
      queryClient.setQueryData(getGetApiJobsIdQueryKey(job.id), updatedJob);
      await queryClient.invalidateQueries({ queryKey: getGetApiJobsQueryKey() });
      setConfirmAction(null);
      setCompletedAction(confirmAction);
```

- [ ] **Step 3: Render the new dialog instead of the undo-rejection dialog**

Find:

```tsx
      {undoRejectionCompleted && (
        <UndoRejectionSuccessDialog
          reportNumber={formatReportNumber(job)}
          onGoToJobList={() => navigate('/app', { replace: true })}
          onGoToJob={() => setUndoRejectionCompleted(false)}
        />
      )}
```

Replace with:

```tsx
      {completedAction && (
        <ActionSuccessDialog
          action={completedAction}
          reportNumber={formatReportNumber(job)}
          onGoToJobList={() => navigate('/app', { replace: true })}
          onGoToJob={() => navigate(`/app/completed/${job.id}`, { replace: true })}
        />
      )}
```

- [ ] **Step 4: Replace the `UndoRejectionSuccessDialog` function with `ActionSuccessDialog`**

Find the whole `UndoRejectionSuccessDialog` function:

```tsx
function UndoRejectionSuccessDialog({
  reportNumber,
  onGoToJobList,
  onGoToJob,
}: {
  reportNumber: string;
  onGoToJobList: () => void;
  onGoToJob: () => void;
}) {
  return createPortal(
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="undo-rejection-success-title">
      <div className="modal-card">
        <h3 id="undo-rejection-success-title">Afvisningen er fortrudt</h3>
        <p>Sagen <strong>{reportNumber}</strong> er sendt til gennemgang igen.</p>
        <div className="modal-actions modal-actions--double">
          <button className="btn btn-secondary" type="button" onClick={onGoToJobList}>
            Til sagslisten
          </button>
          <button className="btn btn-primary" type="button" onClick={onGoToJob}>
            Til sagen
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
```

Replace with:

```tsx
function ActionSuccessDialog({
  action,
  reportNumber,
  onGoToJobList,
  onGoToJob,
}: {
  action: 'approve' | 'reject' | 'undo-reject';
  reportNumber: string;
  onGoToJobList: () => void;
  onGoToJob: () => void;
}) {
  const isUndoReject = action === 'undo-reject';
  const isApprove = action === 'approve';
  const title = isUndoReject
    ? 'Afvisningen er fortrudt'
    : isApprove
      ? 'Sagen er godkendt'
      : 'Sagen er afvist';
  const body = isUndoReject
    ? <>Sagen <strong>{reportNumber}</strong> er sendt til gennemgang igen.</>
    : isApprove
      ? <>Sagen <strong>{reportNumber}</strong> er godkendt.</>
      : <>Sagen <strong>{reportNumber}</strong> er afvist.</>;

  return createPortal(
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="action-success-title">
      <div className="modal-card">
        <h3 id="action-success-title">{title}</h3>
        <p>{body}</p>
        <div className="modal-actions modal-actions--double">
          <button className="btn btn-secondary" type="button" onClick={onGoToJobList}>
            Til sagslisten
          </button>
          <button className="btn btn-primary" type="button" onClick={onGoToJob}>
            Til sagen
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
```

Note: `from` is still used by the `LinkedJobs` component (line ~471), so `const from = ...` stays. The `notify` import stays because it is still used by `handleDownloadPdf`, `handlePreviewPdf`, `handleSaveEdit`, and the error branch of `executeConfirmAction`.

- [ ] **Step 5: Run the new test and verify it passes**

From `src/FE`:

```
npx vitest run src/features/jobs/routes/CompletedJobReport.navigation.test.tsx
```

Expected: all 4 tests PASS.

- [ ] **Step 6: Run the existing jobs-related unit tests**

From `src/FE`:

```
npx vitest run src/features/jobs src/features/auth src/features/superadmin src/features/users src/components
```

Expected: all PASS. The change removes `notify.success` from the approve/reject success path, so any test that asserted a success toast must be reviewed; no current test covers `CompletedJobReport` navigation, so nothing else should break.

- [ ] **Step 7: Run lint, TypeScript check, and production build**

From `src/FE`:

```
npx eslint src/features/jobs/routes/CompletedJobReport.tsx src/features/jobs/routes/CompletedJobReport.navigation.test.tsx
npx tsc -b
npm run build
```

Expected: eslint clean (no errors), `tsc -b` exits 0, `npm run build` completes and emits `dist/`.

- [ ] **Step 8: Commit**

```bash
git add src/FE/src/features/jobs/routes/CompletedJobReport.tsx
git commit -m "wor-240: ask admin to go to list or case after approval/rejection"
```

---

## Task 3: Validation report

**Files:**
- None (read-only verification)

- [ ] **Step 1: Re-run the focused test suite and record exact output**

From `src/FE`:

```
npx vitest run src/features/jobs/routes/CompletedJobReport.navigation.test.tsx
```

Expected: PASS, record test names and counts.

- [ ] **Step 2: Confirm branch state and commit log**

```bash
git status
git log --oneline -5
```

Expected: working tree clean on branch `wor-240-approval-navigation-modal` with the design-spec commit and the two implementation commits.

- [ ] **Step 3: State Playwright status per VALIDATION.md**

The repository has no application Playwright harness (only the unrelated `src/FE/wor-213.validation.spec.ts` PWA check), no configured `playwright.config`, and no non-production admin test identity with a job `InReview` in a running app. Report the change as **implemented but Playwright-unvalidated**, listing the exact missing prerequisite (running API + frontend, seeded non-production `InReview` job, admin identity) and the flow that remains (log in as admin, open an `InReview` job, approve, verify the modal and both buttons, repeat for reject).

Do not claim "validated" for the runtime UI behavior.

---

## Self-review

- **Spec coverage:** approve modal title/body/buttons (Task 2 Step 4), reject modal (Task 2 Step 4), "Til sagslisten" -> `/app` and "Til sagen" -> `/app/completed/:id` (Task 2 Step 3), toast removed and `navigate(from)` removed for this path (Task 2 Step 2), undo-reject keeps its text and now navigates on "Til sagen" (Task 2 Steps 3-4), non-dismissible modal kept (Task 2 Step 4), unit test added (Task 1). No gaps.
- **Placeholder scan:** every step has concrete code or exact commands; no TBD/TODO.
- **Type consistency:** state is `'approve' | 'reject' | 'undo-reject' | null`, dialog `action` prop uses the same union; `mutation.mutateAsync` is `mockResolvedValue(state.job)` in `beforeEach` matching the generated hook's async contract; helper names match across tests.
