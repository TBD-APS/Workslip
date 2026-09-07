import { fireEvent, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { JOB_STEPS } from './jobSteps';
import { StepIndicators } from './JobStepNavigation';

vi.mock('../JobWizardTutorial', () => ({
  JobWizardTutorial: () => null,
}));

const WORK_REASON = 'Vælg mindst én anlægstype.';

// jsdom implements no Element.scrollTo, and the centring effect calls it on the
// strip as soon as the active dot mounts. The patch lives on a shared prototype,
// so it has to be taken back off again instead of leaking into later test files.
const originalScrollTo = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'scrollTo');

beforeEach(() => {
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', {
    configurable: true,
    value: vi.fn(),
  });
});

afterEach(() => {
  if (originalScrollTo) {
    Object.defineProperty(HTMLElement.prototype, 'scrollTo', originalScrollTo);
  } else {
    delete (HTMLElement.prototype as unknown as { scrollTo?: unknown }).scrollTo;
  }
});

type Overrides = {
  currentStep?: number;
  completedSteps?: boolean[];
  blockedReasons?: (string | undefined)[];
  onStepChange?: (step: number) => void;
};

const renderIndicators = ({
  currentStep = 3,
  completedSteps = [true, false, true, false, false, false],
  blockedReasons = [undefined, undefined, WORK_REASON, WORK_REASON, WORK_REASON, WORK_REASON],
  onStepChange = vi.fn(),
}: Overrides = {}) => {
  render(
    <StepIndicators
      currentStep={currentStep}
      onStepChange={onStepChange}
      completedSteps={completedSteps}
      blockedReasons={blockedReasons}
    />,
  );
  return { onStepChange };
};

const dot = (index: number) => {
  const el = document.getElementById(`job-step-${index}`);
  if (!(el instanceof HTMLButtonElement)) throw new Error(`#job-step-${index} was not rendered`);
  return el;
};

describe('StepIndicators step contract', () => {
  it('renders one button per step under the ids Playwright targets', () => {
    renderIndicators();

    expect(JOB_STEPS).toHaveLength(6);
    JOB_STEPS.forEach((step, index) => {
      expect(dot(index)).toHaveTextContent(step.label);
    });
  });

  it('marks only the current step with aria-current', () => {
    renderIndicators({ currentStep: 3 });

    expect(dot(3)).toHaveAttribute('aria-current', 'step');
    expect(document.querySelectorAll('[aria-current="step"]')).toHaveLength(1);
  });
});

describe('StepIndicators completion truthfulness', () => {
  it('does not tick a step you walked past without finishing', () => {
    renderIndicators({ currentStep: 3, completedSteps: [true, false, true, false, false, false] });

    expect(dot(1)).not.toHaveClass('completed');
  });

  it('keeps the tick on a step that is behind you and actually valid', () => {
    renderIndicators({ currentStep: 3, completedSteps: [true, false, true, false, false, false] });

    expect(dot(0)).toHaveClass('completed');
  });

  it('never ticks a step that is still ahead of you', () => {
    renderIndicators({ currentStep: 0, completedSteps: [true, true, true, true, true, true] });

    expect(dot(4)).not.toHaveClass('completed');
  });
});

describe('StepIndicators locked steps', () => {
  it('says in Danish why a step is locked and which step blocks it', () => {
    renderIndicators({ currentStep: 3 });

    const locked = dot(2);
    expect(locked).toHaveClass('blocked');
    expect(locked).not.toHaveAttribute('aria-disabled');
    expect(locked).toHaveAttribute('aria-label', `Kontrolpunkter — låst: ${WORK_REASON}`);
    expect(locked).toHaveAttribute('title', WORK_REASON);
  });

  it('does not lock a dot the reason array carries no message for', () => {
    // The lock and its reason are one fact: a dot with no reason is a dot the
    // click lands on, so it must not read as locked. There is no reason-less
    // locked state to caption, and therefore no default reason to invent.
    renderIndicators({ currentStep: 3, blockedReasons: [] });

    expect(dot(2)).not.toHaveClass('blocked');
    expect(dot(2)).toHaveAttribute('aria-label', 'Kontrolpunkter');
    expect(dot(2)).not.toHaveAttribute('title');
  });

  it('locks nothing when the reason prop is omitted entirely', () => {
    render(
      <StepIndicators
        currentStep={3}
        onStepChange={vi.fn()}
        completedSteps={[true, false, true, false, false, false]}
      />,
    );

    JOB_STEPS.forEach((_, index) => {
      expect(dot(index)).not.toHaveClass('blocked');
      expect(dot(index)).not.toHaveAttribute('title');
    });
    expect(dot(2)).toHaveAttribute('aria-label', 'Kontrolpunkter');
  });

  it('still navigates when a locked step is clicked', () => {
    const { onStepChange } = renderIndicators({ currentStep: 3 });

    fireEvent.click(dot(2));

    expect(onStepChange).toHaveBeenCalledTimes(1);
    expect(onStepChange).toHaveBeenCalledWith(2);
  });

  it('does not call the step you are standing on locked', () => {
    // Admins land on step 3 before steps 0-2 are filled in, so the active dot
    // is routinely one whose predecessor is unfinished.
    renderIndicators({ currentStep: 3, completedSteps: [false, false, false, false, false, false] });

    expect(dot(3)).not.toHaveClass('blocked');
    expect(dot(3)).toHaveAttribute('aria-label', 'Timesedler - aktuelt trin');
    expect(dot(3)).not.toHaveAttribute('title');
  });

  it('leaves a reachable step unlocked and unlabelled', () => {
    renderIndicators({ currentStep: 3 });

    expect(dot(1)).not.toHaveClass('blocked');
    expect(dot(1)).toHaveAttribute('aria-label', 'Anlægstyper');
  });
});

describe('StepIndicators a finished step behind a blocker', () => {
  it('reads as locked rather than as progress', () => {
    // The shape JobDetails really produces: the user stands on step 1
    // (Anlægstyper) and that step is itself unfinished, so findBlockingIssue
    // reports its reason for every step ahead - step 3 included, even though
    // step 3's own data is valid (`completedSteps[3] === true`). Clicking dot 3
    // does not land on step 3: handleStepChange sends the user to the blocking
    // step and shows that step's message.
    //
    // The tick and the lock cannot render on one dot, and that is enforced from
    // both sides: the tick needs `index < currentStep` (JobStepNavigation) and a
    // reason is only ever produced for `index > currentStep` (JobDetails). So a
    // valid but unreachable step reads purely as locked - the tick stays off
    // even though the step itself reports no issues, because the click cannot
    // get there yet.
    renderIndicators({ currentStep: 1, completedSteps: [true, false, true, true, false, false] });

    const finishedButLocked = dot(3);
    expect(finishedButLocked).toHaveClass('blocked');
    expect(finishedButLocked).not.toHaveClass('completed');
    expect(finishedButLocked).toHaveAttribute('aria-label', `Timesedler — låst: ${WORK_REASON}`);
    expect(finishedButLocked).toHaveAttribute('title', WORK_REASON);
  });
});
