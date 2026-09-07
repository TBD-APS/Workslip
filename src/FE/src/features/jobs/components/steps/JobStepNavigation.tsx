import { useEffect, useRef, useState, type ReactNode } from 'react';
import { CheckCircle2, ChevronLeft, ChevronRight } from 'lucide-react';
import { JOB_STEPS } from './jobSteps';
import { useDropdownContext } from '../../../../providers/DropdownContext';
import { useMediaQuery } from '../../../../hooks/useMediaQuery';
import { JobWizardTutorial } from '../JobWizardTutorial';

type StepIndicatorsProps = {
  currentStep: number;
  onStepChange: (step: number) => void;
  completedSteps: boolean[];
  blockedReasons?: (string | undefined)[];
};

export function StepIndicators({
  currentStep,
  onStepChange,
  completedSteps,
  blockedReasons,
}: StepIndicatorsProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const reduce = useMediaQuery('(prefers-reduced-motion: reduce)');

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;
    const activeDot = container.querySelector<HTMLButtonElement>('button[aria-current="step"]');
    if (!activeDot) return;

    const targetLeft = activeDot.offsetLeft - (container.clientWidth - activeDot.clientWidth) / 2;
    container.scrollTo({ left: targetLeft, behavior: reduce ? 'auto' : 'smooth' });
  }, [currentStep, reduce]);

  return (
    <>
      <div className="step-indicators" ref={containerRef}>
        {JOB_STEPS.map((step, index) => {
          const StepIcon = step.icon;
          const isActive = index === currentStep;
          // A step only counts as done when it is behind us AND actually valid:
          // a Diverse job reports "no issues" for steps it skips entirely.
          const isCompleted = index < currentStep && completedSteps[index] === true;
          // Lock state and its reason are ONE fact, computed once in JobDetails:
          // `blockedReasons[index]` is the message of the first issue in exactly
          // the range a click has to walk, so a dot carries a reason precisely
          // when that walk finds a blocker - its styling, its accessible name,
          // its tooltip and the step the click lands on cannot contradict each
          // other. A locked dot therefore always has a message to show
          // (JobValidationIssue.message is a required string), so there is no
          // reason-less lock to caption and no default to fall back to. The step
          // you are standing on is never locked, even when something behind it
          // is unfinished.
          const blockedReason = isActive ? undefined : blockedReasons?.[index];
          const isBlocked = blockedReason !== undefined;
          return (
            <button
              id={`job-step-${index}`}
              key={step.label}
              className={`step-dot ${isActive ? 'active' : ''} ${isCompleted ? 'completed' : ''} ${isBlocked ? 'blocked' : ''}`}
              onClick={() => onStepChange(index)}
              title={isBlocked ? blockedReason : undefined}
              aria-label={
                isActive
                  ? `${step.label} - aktuelt trin`
                  : isBlocked
                    ? `${step.label} — låst: ${blockedReason}`
                    : step.label
              }
              aria-current={isActive ? 'step' : undefined}
            >
              <StepIcon size={14} />
              <span className="step-label">{step.label}</span>
            </button>
          );
        })}
      </div>
      <JobWizardTutorial currentStep={currentStep} />
    </>
  );
}

type StepNavigationProps = {
  currentStep: number;
  isLastStep: boolean;
  onBack: () => void;
  onNext: () => void;
  onDone: () => void;
  onNextBlocked?: () => void;
  backLabel?: string;
  blockedNextLabel?: string;
  doneLabel?: string;
  doneIcon?: ReactNode;
  disableNext?: boolean;
  disableDone?: boolean;
  nextDisabledReason?: string;
  doneDisabledReason?: string;
  statusSlot?: ReactNode;
  hideDoneButton?: boolean;
};

export function StepNavigation({
  currentStep,
  isLastStep,
  onBack,
  onNext,
  onDone,
  onNextBlocked,
  backLabel = 'Tilbage',
  blockedNextLabel,
  doneLabel = 'Færdig',
  doneIcon = <CheckCircle2 size={18} />,
  disableNext = false,
  disableDone = false,
  nextDisabledReason,
  doneDisabledReason,
  statusSlot,
  hideDoneButton = false,
}: StepNavigationProps) {
  const { openDropdowns } = useDropdownContext();
  const [hidden, setHidden] = useState(false);
  const anchorRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const container = document.querySelector('.app-shell');
    const target = container ?? window;

    const getScrollTop = () =>
      container ? (container as HTMLElement).scrollTop : window.scrollY;

    const getScrollBottom = () => {
      if (container) {
        const el = container as HTMLElement;
        return el.scrollHeight - el.scrollTop - el.clientHeight;
      }
      return document.documentElement.scrollHeight - window.scrollY - window.innerHeight;
    };

    let lastScrollY = getScrollTop();
    let downAccum = 0;
    let revealTimer: ReturnType<typeof setTimeout> | undefined;

    const update = () => {
      const scrollTop = getScrollTop();

      // Never pull the bar out from under a keyboard user who has tabbed into it.
      // The accumulator resets with it: 48px has to mean 48px of continuous
      // downward intent, not a total carried across a visit to the bar.
      if (anchorRef.current?.contains(document.activeElement)) {
        setHidden(false);
        downAccum = 0;
        lastScrollY = scrollTop;
        return;
      }

      // The bar is never permanently gone: half a second after scrolling stops
      // it always comes back, whatever the accumulated direction said.
      clearTimeout(revealTimer);
      revealTimer = setTimeout(() => {
        downAccum = 0;
        setHidden(false);
      }, 500);

      const scrollBottom = getScrollBottom();
      const d = scrollTop - lastScrollY;

      if (scrollBottom <= 24) {
        downAccum = 0;
        setHidden(false);
      } else if (scrollTop > 5 && d > 0) {
        // Accumulate intent: a rubber-band bounce or a keyboard-open viewport
        // resize is a pixel or two and must not hide the bar.
        downAccum += d;
        if (downAccum > 48) setHidden(true);
      } else if (d < 0) {
        downAccum = 0;
        setHidden(false);
      }

      lastScrollY = scrollTop;
    };

    // One frame per burst, and the handle is kept: `update` arms an idle-reveal
    // timer on every call, so a frame that landed after unmount would both
    // setHidden on a dead tree and arm a timer this cleanup can no longer clear.
    let frame = 0;
    const onScroll = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(update);
    };
    target.addEventListener('scroll', onScroll, { passive: true });
    update();

    return () => {
      target.removeEventListener('scroll', onScroll);
      cancelAnimationFrame(frame);
      clearTimeout(revealTimer);
    };
  }, []);

  const actionableBlockedNext = disableNext && Boolean(onNextBlocked);
  const nextLabel = disableNext ? blockedNextLabel ?? 'Ret oplysninger' : 'Næste';

  const bar = (
    <div className="step-nav">
      <button
        id="job-step-back"
        className="step-nav-btn step-nav-btn-back"
        onClick={onBack}
        aria-label={backLabel}
      >
        <ChevronLeft size={18} />
        <span>{backLabel}</span>
      </button>

      <span className="step-nav-counter">Trin {currentStep + 1} / {JOB_STEPS.length}</span>

      {!isLastStep ? (
        <button
          id="job-step-next"
          className="step-nav-btn step-nav-btn-next"
          onClick={() => {
            if (actionableBlockedNext) {
              onNextBlocked?.();
              return;
            }
            onNext();
          }}
          disabled={disableNext && !onNextBlocked}
          aria-disabled={disableNext && !onNextBlocked ? true : undefined}
          title={disableNext ? nextDisabledReason : undefined}
          aria-label={disableNext ? `${nextLabel} — ${nextDisabledReason ?? 'ret manglende oplysninger'}` : 'Næste'}
        >
          <span>{nextLabel}</span>
          <ChevronRight size={18} />
        </button>
      ) : !hideDoneButton ? (
        <button
          id="job-step-done"
          className="step-nav-btn step-nav-btn-next"
          onClick={onDone}
          disabled={disableDone}
          title={disableDone ? doneDisabledReason : undefined}
          aria-label={disableDone ? `${doneLabel} — ${doneDisabledReason ?? 'ikke tilgængelig'}` : doneLabel}
        >
          {doneIcon}
          <span>{doneLabel}</span>
        </button>
      ) : (
        // The counter is `flex: 1; text-align: center`, so it only sits in the
        // middle while both side slots are the same width. This spacer carries
        // no text any more, so it has to hold the width the back button's
        // icon+label occupies (~7rem at --fs-sm) on its own.
        <div
          className="step-nav-btn"
          aria-hidden="true"
          style={{ visibility: 'hidden', pointerEvents: 'none', minWidth: '7rem' }}
        />
      )}
    </div>
  );

  const isDropdownOpen = openDropdowns > 0;
  const hiddenNow = hidden || isDropdownOpen;

  return (
    <div ref={anchorRef} className={`step-nav-anchor${hiddenNow ? ' is-hidden' : ''}`}>
      {statusSlot && <div style={{ display: 'flex', justifyContent: 'center', marginBottom: '0.5rem' }}>{statusSlot}</div>}
      {bar}
    </div>
  );
}
