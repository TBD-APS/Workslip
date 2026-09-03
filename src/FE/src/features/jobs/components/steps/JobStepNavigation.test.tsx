import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { StepNavigation } from './JobStepNavigation';

// Mutable so a case can open a dropdown before render; hoisted because the mock
// factory below is lifted above the imports.
const dropdownState = vi.hoisted(() => ({ openDropdowns: 0 }));

vi.mock('../../../../providers/DropdownContext', () => ({
  useDropdownContext: () => dropdownState,
}));

vi.mock('../JobWizardTutorial', () => ({
  JobWizardTutorial: () => null,
}));

beforeEach(() => {
  dropdownState.openDropdowns = 0;
});

const renderNav = () =>
  render(
    <StepNavigation
      currentStep={0}
      isLastStep={false}
      onBack={vi.fn()}
      onNext={vi.fn()}
      onDone={vi.fn()}
    />,
  );

const anchor = () => {
  const el = document.querySelector<HTMLDivElement>('.step-nav-anchor');
  if (!el) throw new Error('.step-nav-anchor was not rendered');
  return el;
};

describe('StepNavigation actionable validation', () => {
  it('keeps a blocked next action operable and labels the corrective action', () => {
    const onNext = vi.fn();
    const onNextBlocked = vi.fn();

    render(
      <StepNavigation
        currentStep={0}
        isLastStep={false}
        onBack={vi.fn()}
        onNext={onNext}
        onNextBlocked={onNextBlocked}
        blockedNextLabel="Udfyld kundenavn"
        onDone={vi.fn()}
        disableNext
        nextDisabledReason="Kundenavn mangler."
      />,
    );

    const next = screen.getByRole('button', { name: /udfyld kundenavn — kundenavn mangler/i });
    expect(next).toHaveTextContent('Udfyld kundenavn');
    expect(next).not.toHaveAttribute('aria-disabled');
    expect(next).not.toBeDisabled();

    fireEvent.click(next);

    expect(onNextBlocked).toHaveBeenCalledTimes(1);
    expect(onNext).not.toHaveBeenCalled();
  });

  it('keeps legacy disabled behavior when a caller provides no corrective action', () => {
    render(
      <StepNavigation
        currentStep={0}
        isLastStep={false}
        onBack={vi.fn()}
        onNext={vi.fn()}
        onDone={vi.fn()}
        disableNext
        nextDisabledReason="Mangler oplysninger"
      />,
    );

    expect(screen.getByRole('button', { name: /ret oplysninger — mangler oplysninger/i })).toBeDisabled();
  });
});

describe('StepNavigation back button label', () => {
  it('defaults the back button to Tilbage', () => {
    renderNav();

    const back = screen.getByRole('button', { name: 'Tilbage' });
    expect(back).toHaveAttribute('id', 'job-step-back');
    expect(back).toHaveTextContent('Tilbage');
  });

  it('says where the button actually goes on the first step', () => {
    render(
      <StepNavigation
        currentStep={0}
        isLastStep={false}
        backLabel="Til oversigten"
        onBack={vi.fn()}
        onNext={vi.fn()}
        onDone={vi.fn()}
      />,
    );

    const back = screen.getByRole('button', { name: 'Til oversigten' });
    expect(back).toHaveAttribute('id', 'job-step-back');
    expect(back).toHaveTextContent('Til oversigten');
    expect(screen.queryByRole('button', { name: 'Tilbage' })).toBeNull();
  });

  it('leaves no stray Tilbage label in the last-step spacer', () => {
    render(
      <StepNavigation
        currentStep={5}
        isLastStep
        hideDoneButton
        onBack={vi.fn()}
        onNext={vi.fn()}
        onDone={vi.fn()}
      />,
    );

    // Only the real back button carries the word - the spacer is empty.
    expect(screen.queryAllByText('Tilbage')).toHaveLength(1);
    expect(screen.getAllByText('Tilbage')[0].closest('button')).toHaveAttribute(
      'id',
      'job-step-back',
    );
  });

  it('keeps the width that centres the counter in the emptied spacer', () => {
    const { container } = render(
      <StepNavigation
        currentStep={5}
        isLastStep
        hideDoneButton
        onBack={vi.fn()}
        onNext={vi.fn()}
        onDone={vi.fn()}
      />,
    );

    // `.step-nav-counter` is `flex: 1; text-align: center`, so an empty spacer
    // with padding only would shove 'Trin 6 / 6' off centre on the last step.
    const spacer = container.querySelector<HTMLDivElement>('.step-nav-btn[aria-hidden="true"]');
    expect(spacer).not.toBeNull();
    expect(spacer).toHaveStyle({ minWidth: '7rem', visibility: 'hidden' });
    expect(spacer).toBeEmptyDOMElement();
  });
});

describe('StepNavigation scroll visibility', () => {
  const SCROLL_HEIGHT = 5000;
  const CLIENT_HEIGHT = 800;
  // scrollTop at which scrollBottom reaches 0.
  const MAX_SCROLL_TOP = SCROLL_HEIGHT - CLIENT_HEIGHT;

  let shell: HTMLDivElement;
  let scrollTop = 0;

  beforeEach(() => {
    // The scroll handler is wrapped in requestAnimationFrame, so the fake clock
    // has to own rAF as well as the idle-reveal timeout.
    vi.useFakeTimers({
      toFake: ['setTimeout', 'clearTimeout', 'requestAnimationFrame', 'cancelAnimationFrame'],
    });

    // StepNavigation resolves `.app-shell` once, at mount: the container must
    // already be in the document before render.
    scrollTop = 0;
    shell = document.createElement('div');
    shell.className = 'app-shell';
    Object.defineProperty(shell, 'scrollHeight', { configurable: true, value: SCROLL_HEIGHT });
    Object.defineProperty(shell, 'clientHeight', { configurable: true, value: CLIENT_HEIGHT });
    // jsdom performs no layout, so scrollTop reads a constant 0 unless we own it.
    Object.defineProperty(shell, 'scrollTop', {
      configurable: true,
      get: () => scrollTop,
      set: (next: number) => {
        scrollTop = next;
      },
    });
    document.body.appendChild(shell);
  });

  afterEach(() => {
    shell.remove();
    vi.useRealTimers();
  });

  const scrollTo = (next: number) => {
    shell.scrollTop = next;
    act(() => {
      fireEvent.scroll(shell);
      // One animation frame is enough to run the handler, and 20ms stays far
      // inside the 500ms idle reveal.
      vi.advanceTimersByTime(20);
    });
  };

  it('ignores a down-scroll too small to be intent', () => {
    renderNav();

    scrollTo(20);

    expect(anchor()).not.toHaveClass('is-hidden');
  });

  it('hides the bar once accumulated downward scrolling passes the threshold', () => {
    renderNav();

    scrollTo(60);

    expect(anchor()).toHaveClass('is-hidden');
  });

  it('reveals the bar again on the first upward scroll', () => {
    renderNav();
    scrollTo(60);
    expect(anchor()).toHaveClass('is-hidden');

    scrollTo(50);

    expect(anchor()).not.toHaveClass('is-hidden');
  });

  it('reveals the bar near the bottom of the scroll container', () => {
    renderNav();
    scrollTo(60);
    expect(anchor()).toHaveClass('is-hidden');

    // scrollBottom of 20, inside the 24px reveal band.
    scrollTo(MAX_SCROLL_TOP - 20);

    expect(anchor()).not.toHaveClass('is-hidden');
  });

  it('brings the bar back half a second after scrolling stops', () => {
    renderNav();
    scrollTo(60);
    expect(anchor()).toHaveClass('is-hidden');

    act(() => {
      vi.advanceTimersByTime(500);
    });

    expect(anchor()).not.toHaveClass('is-hidden');
  });

  it('never pulls the bar out from under a focused navigation button', () => {
    renderNav();
    const next = document.getElementById('job-step-next');
    if (!(next instanceof HTMLButtonElement)) throw new Error('#job-step-next was not rendered');
    next.focus();

    scrollTo(60);

    expect(anchor()).not.toHaveClass('is-hidden');
    expect(document.activeElement).toBe(next);
  });

  it('does not carry accumulated downward scrolling across a focus visit', () => {
    renderNav();
    const next = document.getElementById('job-step-next');
    if (!(next instanceof HTMLButtonElement)) throw new Error('#job-step-next was not rendered');

    // 40px of intent: real, but still under the 48px threshold.
    scrollTo(40);
    expect(anchor()).not.toHaveClass('is-hidden');

    next.focus();
    scrollTo(45);
    next.blur();

    // Only 10px of fresh intent after the visit, so the bar has to stay.
    scrollTo(55);

    expect(anchor()).not.toHaveClass('is-hidden');
  });

  it('cancels the pending frame and the idle-reveal timer on unmount', () => {
    const { unmount } = renderNav();

    shell.scrollTop = 60;
    fireEvent.scroll(shell);

    // The mount-time update armed a reveal timer and the scroll queued a frame.
    expect(vi.getTimerCount()).toBeGreaterThan(0);

    unmount();

    // Neither may survive: the frame would setHidden on a dead tree and arm a
    // reveal timer the cleanup can no longer reach.
    expect(vi.getTimerCount()).toBe(0);
  });

  it('hides via the class, not an inline pointer-events style', () => {
    renderNav();
    expect(anchor().style.pointerEvents).toBe('');

    scrollTo(60);

    expect(anchor()).toHaveClass('is-hidden');
    expect(anchor().style.pointerEvents).toBe('');
  });
});

describe('StepNavigation dropdown deference', () => {
  it('hides the bar while a dropdown overlay is open', () => {
    dropdownState.openDropdowns = 1;

    renderNav();

    expect(anchor()).toHaveClass('is-hidden');
    expect(anchor().style.pointerEvents).toBe('');
  });
});
