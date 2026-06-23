import { useEffect, useRef, useState, type ReactNode } from 'react';
import { CheckCircle2, ChevronLeft, ChevronRight } from 'lucide-react';
import { JOB_STEPS } from './jobSteps';
import { useDropdownContext } from '../../../../providers/DropdownContext';

const HIDE_DELAY_MS = 10;
const SCROLL_THRESHOLD = 5;

type StepIndicatorsProps = {
  currentStep: number;
  onStepChange: (step: number) => void;
  completedSteps: boolean[];
};

export function StepIndicators({ currentStep, onStepChange, completedSteps }: StepIndicatorsProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;
    const activeDot = container.querySelector<HTMLButtonElement>('button[aria-current="step"]');
    if (!activeDot) return;

    const targetLeft = activeDot.offsetLeft - (container.clientWidth - activeDot.clientWidth) / 2;
    container.scrollTo({ left: targetLeft, behavior: 'smooth' });
  }, [currentStep]);

  return (
    <div className="step-indicators" ref={containerRef}>
      {JOB_STEPS.map((step, index) => {
        const StepIcon = step.icon;
        const isActive = index === currentStep;
        const isCompleted = index < currentStep;
        const isDisabled = (index === 3 && !completedSteps[2]) || (index === 5 && !completedSteps[4]);
        return (
          <button
            key={step.label}
            className={`step-dot ${isActive ? 'active' : ''} ${isCompleted ? 'completed' : ''}`}
            onClick={() => onStepChange(index)}
            aria-disabled={isDisabled || undefined}
            aria-label={isActive ? `${step.label} - aktuelt trin` : step.label}
            aria-current={isActive ? 'step' : undefined}
          >
            <StepIcon size={14} />
            <span className="step-label">{step.label}</span>
          </button>
        );
      })}
    </div>
  );
}

type StepNavigationProps = {
  currentStep: number;
  isLastStep: boolean;
  onBack: () => void;
  onNext: () => void;
  onDone: () => void;
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
  doneLabel = 'Færdig',
  doneIcon = <CheckCircle2 size={18} />,
  disableNext = false,
  disableDone = false,
  nextDisabledReason,
  doneDisabledReason,
  statusSlot,
  hideDoneButton = false,
}: StepNavigationProps) {
  const [scrollState, setScrollState] = useState<'visible' | 'hidden'>('visible');
  const { openDropdowns } = useDropdownContext();
  const stateRef = useRef(scrollState);
  const lastScrollY = useRef(0);
  const hideTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const rafId = useRef(0);

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

    const go = (next: 'visible' | 'hidden') => {
      if (next !== stateRef.current) {
        stateRef.current = next;
        setScrollState(next);
      }
    };

    const update = () => {
      const scrollTop = getScrollTop();
      const scrollBottom = getScrollBottom();
      const atBottom = scrollBottom <= 24;
      const d = scrollTop - lastScrollY.current;

      if (atBottom) {
        clearTimeout(hideTimer.current);
        go('visible');
      } else {
        const scrollingDown = scrollTop > SCROLL_THRESHOLD && d > 0;

        if (scrollingDown && stateRef.current === 'visible') {
          clearTimeout(hideTimer.current);
          hideTimer.current = setTimeout(() => go('hidden'), HIDE_DELAY_MS);
        }

        if (d < 0 && stateRef.current !== 'visible') {
          clearTimeout(hideTimer.current);
          go('visible');
        }
      }

      lastScrollY.current = scrollTop;
    };

    const onScroll = () => {
      cancelAnimationFrame(rafId.current);
      rafId.current = requestAnimationFrame(update);
    };

    target.addEventListener('scroll', onScroll, { passive: true });
    update();

    return () => {
      target.removeEventListener('scroll', onScroll);
      cancelAnimationFrame(rafId.current);
      clearTimeout(hideTimer.current);
    };
  }, []);

  const bar = (
    <div className="step-nav">
      <button
        className="step-nav-btn step-nav-btn-back"
        onClick={onBack}
        aria-label="Tilbage"
      >
        <ChevronLeft size={18} />
        <span>Tilbage</span>
      </button>

      <span className="step-nav-counter">Trin {currentStep + 1} / {JOB_STEPS.length}</span>

      {!isLastStep ? (
        <button
          className="step-nav-btn step-nav-btn-next"
          onClick={onNext}
          disabled={disableNext}
          title={disableNext ? nextDisabledReason : undefined}
          aria-label={disableNext ? `Næste — ${nextDisabledReason ?? 'ikke tilgængelig'}` : 'Næste'}
        >
          <span>Næste</span>
          <ChevronRight size={18} />
        </button>
      ) : !hideDoneButton ? (
        <button
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
        <div className="step-nav-btn" style={{ visibility: 'hidden', pointerEvents: 'none' }}>
          <ChevronLeft size={18} />
          <span>Tilbage</span>
        </div>
      )}
    </div>
  );

  const isDropdownOpen = openDropdowns > 0;

  return (
    <div
      style={{
        position: 'sticky',
        bottom: 'calc(80px + 1rem)',
        zIndex: 150,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        opacity: scrollState === 'hidden' || isDropdownOpen ? 0 : 1,
        pointerEvents: scrollState === 'hidden' || isDropdownOpen ? 'none' : 'auto',
        transition: 'opacity 0.1s ease',
      }}
    >
      {statusSlot && <div style={{ display: 'flex', justifyContent: 'center', marginBottom: '0.5rem' }}>{statusSlot}</div>}
      {bar}
    </div>
  );
}
