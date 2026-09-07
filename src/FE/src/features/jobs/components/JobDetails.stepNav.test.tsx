import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { RouterProvider, createMemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { JobStatus } from '../../../api/generated/models';
import type { useJobDetails } from '../hooks/useJobDetails';
import { JobDetailsPage } from './JobDetails';

vi.mock('../../../api/generated/jobs/jobs', () => ({
  useDeleteApiJobsId: () => ({ mutate: vi.fn() }),
  getGetApiJobsQueryKey: () => ['jobs'],
}));

vi.mock('../../../providers/permissions', () => ({
  useCan: () => false,
  useIsAdmin: () => false,
}));

// The real hook throws without a provider, and StepNavigation is NOT mocked here.
vi.mock('../../../providers/DropdownContext', () => ({
  useDropdownContext: () => ({ openDropdowns: 0 }),
}));

// Spread the real module: JobDetails may start importing another util from here,
// and a factory that lists only these two turns that into "undefined is not a
// function" in a test that has nothing to do with the new import.
vi.mock('../utils', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../utils')>()),
  isValidJobForm: () => true,
  isValidWork: () => true,
}));

vi.mock('./steps/controlPointsValidation', () => ({
  validateControlPoints: () => ({ valid: true }),
}));

vi.mock('./steps/JobOverviewStep', () => ({
  JobOverviewStep: () => <div>overview-step</div>,
}));

vi.mock('./steps/WorkCategoryStep', () => ({
  WorkCategoryStep: () => <div>work-step</div>,
}));

vi.mock('./steps/ControlPointsStep', () => ({
  ControlPointsStep: () => <div>control-points-step</div>,
}));

vi.mock('./steps/JobWorksheetsStep', () => ({
  JobWorksheetsStep: () => <div>worksheets-step</div>,
}));

vi.mock('./steps/JobCompletionStep', () => ({
  JobCompletionStep: () => <div>completion-step</div>,
}));

vi.mock('./steps/JobAttestationStep', () => ({
  JobAttestationStep: () => <div>attestation-step</div>,
}));

vi.mock('./JobWizardTutorial', () => ({
  JobWizardTutorial: () => null,
}));

vi.mock('./JobHistoryDrawer', () => ({
  JobHistoryDrawer: () => null,
}));

vi.mock('./JobConversationLauncher', () => ({
  JobConversationLauncher: () => null,
}));

vi.mock('../../../components/common/ConfirmDeleteDialog', () => ({
  ConfirmDeleteDialog: () => null,
}));

vi.mock('../../../components/common/DeleteButton', () => ({
  DeleteButton: () => null,
}));

type StubOptions = {
  currentStep?: number;
  work?: Record<string, unknown>;
  worksheets?: unknown[];
  customerName?: string;
  // Whether the hook accepts the move. The real `navigateToStep` returns a
  // boolean - true when the step actually changed, false when it refused - and
  // JobDetails only moves focus on a true, so a stub must answer honestly.
  navigateToStep?: (step: number) => boolean;
};

function createDetailsStub({
  currentStep = 0,
  work = {},
  worksheets = [],
  customerName = 'Kunde A/S',
  navigateToStep = () => true,
}: StubOptions = {}) {
  return {
    job: { id: 'job-1', reportNumber: '1234', jobType: 'KLS', status: JobStatus.Draft },
    form: {
      reportNumber: '1234',
      jobType: 'KLS',
      // Step 0 has to be clean, otherwise every forward gate stops there.
      customerSnapshot: { name: customerName, email: '', phone: '' },
      work: {
        categoryIds: [],
        workKind: '',
        customWorkKind: '',
        controlPointSelections: {},
        irrelevantCategoryIds: [],
        closureFlags: [],
        ...work,
      },
    },
    referenceData: { installationTypes: [], workKinds: [] },
    currentStep,
    setCurrentStep: vi.fn(),
    jumpToStep: vi.fn(),
    isLoading: false,
    isError: false,
    isSubmittingJob: false,
    saveStatus: 'idle' as const,
    assignmentStatus: 'idle' as const,
    linksStatus: 'idle' as const,
    hasUnsavedChanges: true,
    reportNumberReadOnly: true,
    worksheets,
    isLoadingUsers: false,
    isLoadingReferenceData: false,
    isSavingWorksheet: false,
    isDeletingWorksheet: false,
    isAdmin: false,
    saveCurrentStep: vi.fn(),
    saveAllChanges: vi.fn(),
    discardChanges: vi.fn(),
    navigateToStep: vi.fn(navigateToStep),
    flushSave: vi.fn(),
    upsertWorksheet: vi.fn(),
    deleteWorksheet: vi.fn(),
    assignableUsers: [],
    assignedUserIds: [],
    linkableJobs: [],
    linkedJobIds: [],
    updateWorkCategories: vi.fn(),
    updateWorkKind: vi.fn(),
    updateCustomWorkKind: vi.fn(),
    toggleControlPoint: vi.fn(),
    toggleCategoryIrrelevant: vi.fn(),
    updateAllIrrelevantReason: vi.fn(),
    updateClosureFlags: vi.fn(),
    selectCustomer: vi.fn(),
    updateSnapshotField: vi.fn(),
    updateEditSnapshot: vi.fn(),
    updateCreateCustomer: vi.fn(),
    updateDestinationAddress: vi.fn(),
    updateDestinationZipCode: vi.fn(),
    updateDestinationCity: vi.fn(),
    updateTaskDescription: vi.fn(),
    updateCustomerObservations: vi.fn(),
    updateTechnicalObservations: vi.fn(),
    updateAssignedUsers: vi.fn(),
    updateLinkedJobs: vi.fn(),
    submitJob: vi.fn(),
    submitJobFieldErrors: [],
    saveCurrentStepAndSetCurrentStep: vi.fn(),
  } as unknown as ReturnType<typeof useJobDetails>;
}

function renderPage(options: StubOptions = {}) {
  const details = createDetailsStub(options);
  const onBack = vi.fn();
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      {
        path: '/app/job/:id',
        element: (
          <JobDetailsPage
            details={details}
            onBack={onBack}
            onDone={vi.fn()}
            onGoToReport={vi.fn()}
          />
        ),
      },
    ],
    { initialEntries: ['/app/job/job-1'] },
  );
  const { container } = render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return { details, onBack, container };
}

const FILLED_WORK = {
  categoryIds: ['installation-1'],
  workKind: 'kontrol',
  controlPointSelections: { 'cp-1': true },
  closureFlags: ['Færdig'],
};

// jsdom implements no Element.scrollTo, so it has to be installed - but the patch
// lives on a shared prototype and must be taken back off again, or it leaks into
// every later test file that happens to run in this worker.
const originalScrollTo = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'scrollTo');

beforeEach(() => {
  vi.clearAllMocks();
  // The step strip centres the active dot on mount.
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', {
    configurable: true,
    value: vi.fn(),
  });
});

afterEach(cleanup);

afterEach(() => {
  if (originalScrollTo) {
    Object.defineProperty(HTMLElement.prototype, 'scrollTo', originalScrollTo);
  } else {
    delete (HTMLElement.prototype as unknown as { scrollTo?: unknown }).scrollTo;
  }
});

describe('JobDetailsPage wizard back button', () => {
  it('leaves the wizard from the first step and says so', () => {
    const { details, onBack, container } = renderPage({ currentStep: 0 });

    const back = container.querySelector<HTMLButtonElement>('#job-step-back');
    expect(back).toHaveTextContent('Til oversigten');

    fireEvent.click(back!);

    expect(onBack).toHaveBeenCalledTimes(1);
    expect(details.navigateToStep).not.toHaveBeenCalled();
  });

  it('steps back one step from the middle of the wizard', () => {
    const { details, onBack, container } = renderPage({ currentStep: 2 });

    const back = container.querySelector<HTMLButtonElement>('#job-step-back');
    expect(back).toHaveTextContent('Tilbage');

    fireEvent.click(back!);

    expect(details.navigateToStep).toHaveBeenCalledTimes(1);
    expect(details.navigateToStep).toHaveBeenCalledWith(1);
    expect(onBack).not.toHaveBeenCalled();
  });

  it('renders no stray Tilbage label in the last-step spacer', () => {
    renderPage({ currentStep: 5, work: FILLED_WORK, worksheets: [{ id: 'ws-1' }] });

    const labels = screen.queryAllByText('Tilbage');
    expect(labels).toHaveLength(1);
    expect(labels[0].closest('button')).toHaveAttribute('id', 'job-step-back');
  });
});

describe('JobDetailsPage step focus', () => {
  const nextFrame = async () => {
    await act(async () => {
      await new Promise((resolve) => {
        requestAnimationFrame(() => resolve(null));
      });
    });
  };

  it('moves focus into the new step when Næste is pressed', async () => {
    const { container } = renderPage({
      currentStep: 2,
      work: FILLED_WORK,
      worksheets: [{ id: 'ws-1' }],
    });

    fireEvent.click(container.querySelector<HTMLButtonElement>('#job-step-next')!);
    await nextFrame();

    expect(document.activeElement).toBe(container.querySelector('#job-step-content'));
  });

  it('moves focus into the new step when Tilbage is pressed', async () => {
    const { container } = renderPage({ currentStep: 2 });

    fireEvent.click(container.querySelector<HTMLButtonElement>('#job-step-back')!);
    await nextFrame();

    expect(document.activeElement).toBe(container.querySelector('#job-step-content'));
  });

  it('moves focus into the new step when a step dot jump goes through', async () => {
    const { details, container } = renderPage({
      currentStep: 0,
      work: FILLED_WORK,
      worksheets: [{ id: 'ws-1' }],
    });

    fireEvent.click(container.querySelector<HTMLButtonElement>('#job-step-3')!);
    await nextFrame();

    expect(details.navigateToStep).toHaveBeenCalledWith(3);
    expect(document.activeElement).toBe(container.querySelector('#job-step-content'));
  });

  it('leaves focus on the pressed control when the hook refuses the move', async () => {
    // The boolean exists for exactly this case: the hook can refuse a move it
    // was asked to make (same step, or its own validation gate). Focus must
    // then stay on the control the user pressed - pulling it into a region that
    // still names the old step would announce a move that never happened.
    const { details, container } = renderPage({
      currentStep: 2,
      work: FILLED_WORK,
      worksheets: [{ id: 'ws-1' }],
      navigateToStep: () => false,
    });

    const next = container.querySelector<HTMLButtonElement>('#job-step-next')!;
    next.focus();
    fireEvent.click(next);
    await nextFrame();

    expect(details.navigateToStep).toHaveBeenCalledWith(3);
    expect(document.activeElement).toBe(next);
    expect(document.activeElement).not.toBe(container.querySelector('#job-step-content'));
  });

  it('names the step in the content region so the move is announced', () => {
    const { container } = renderPage({ currentStep: 2 });

    const region = container.querySelector('#job-step-content')!;
    expect(region).toHaveAttribute('role', 'region');
    expect(region).toHaveAttribute('aria-label', 'Trin 3 af 6: Kontrolpunkter');
  });

  it('leaves the validation bounce to focus the offending field, not the region', async () => {
    const { container } = renderPage({ currentStep: 0 });

    fireEvent.click(container.querySelector<HTMLButtonElement>('#job-step-3')!);
    await nextFrame();

    expect(document.activeElement).not.toBe(container.querySelector('#job-step-content'));
  });
});

describe('JobDetailsPage forward gate', () => {
  it('bounces a jump past an unfinished step to the step that blocks it', () => {
    const { details, container } = renderPage({ currentStep: 0 });

    fireEvent.click(container.querySelector<HTMLButtonElement>('#job-step-3')!);

    expect(details.navigateToStep).not.toHaveBeenCalled();
    expect(details.jumpToStep).toHaveBeenCalledTimes(1);
    expect(details.jumpToStep).toHaveBeenCalledWith(1);
  });

  it('lets a jump through once every step in between is valid', () => {
    const { details, container } = renderPage({
      currentStep: 0,
      work: FILLED_WORK,
      worksheets: [{ id: 'ws-1' }],
    });

    fireEvent.click(container.querySelector<HTMLButtonElement>('#job-step-3')!);

    expect(details.jumpToStep).not.toHaveBeenCalled();
    expect(details.navigateToStep).toHaveBeenCalledTimes(1);
    expect(details.navigateToStep).toHaveBeenCalledWith(3);
  });

  it('names the step the click actually bounces to, not the previous step', () => {
    // Both step 0 and step 1 are unfinished. Dot 2 is locked because step 1 is
    // incomplete, but the click lands on step 0 - so step 0 is the reason.
    const { details, container } = renderPage({ currentStep: 0, customerName: '' });

    const locked = container.querySelector<HTMLButtonElement>('#job-step-2')!;
    expect(locked).toHaveClass('blocked');
    expect(locked).toHaveAttribute('aria-label', 'Kontrolpunkter — låst: Kundenavn mangler.');
    expect(locked).toHaveAttribute('title', 'Kundenavn mangler.');

    fireEvent.click(locked);

    expect(details.jumpToStep).toHaveBeenCalledWith(0);
    expect(details.navigateToStep).not.toHaveBeenCalled();
  });

  it('gates the status dot shortcut to attestation through the same check', () => {
    const { details } = renderPage({ currentStep: 0 });

    fireEvent.click(screen.getByRole('button', { name: 'Til gennemsyn, vælg status' }));

    expect(details.jumpToStep).toHaveBeenCalledWith(1);
    expect(details.navigateToStep).not.toHaveBeenCalledWith(5);
  });
});
