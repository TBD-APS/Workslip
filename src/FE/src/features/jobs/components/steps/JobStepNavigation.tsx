import { useEffect, useRef, useState, type ReactNode } from 'react';
import { CheckCircle2, ChevronLeft, ChevronRight } from 'lucide-react';
import { JOB_STEPS } from './jobSteps';
import { useDropdownContext } from '../../../../providers/DropdownContext';

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
        const isDisabled = index > 0 && !completedSteps[index - 1];
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
  const { openDropdowns } = useDropdownContext();
  const [hidden, setHidden] = useState(false);

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
    let hideTimer: ReturnType<typeof setTimeout> | undefined;

    const update = () => {
      const scrollTop = getScrollTop();
      const scrollBottom = getScrollBottom();
      const d = scrollTop - lastScrollY;

      if (scrollBottom <= 24) {
        clearTimeout(hideTimer);
        setHidden(false);
      } else if (scrollTop > 5 && d > 0) {
        clearTimeout(hideTimer);
        hideTimer = setTimeout(() => setHidden(true), 10);
      } else if (d < 0) {
        clearTimeout(hideTimer);
        setHidden(false);
      }

      lastScrollY = scrollTop;
    };

    const onScroll = () => requestAnimationFrame(update);
    target.addEventListener('scroll', onScroll, { passive: true });
    update();

    return () => {
      target.removeEventListener('scroll', onScroll);
      clearTimeout(hideTimer);
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
  const hiddenNow = hidden || isDropdownOpen;

  return (
    <div
      className={`step-nav-anchor${hiddenNow ? ' is-hidden' : ''}`}
      style={{
        pointerEvents: hiddenNow ? 'none' : 'auto',
      }}
    >
      {statusSlot && <div style={{ display: 'flex', justifyContent: 'center', marginBottom: '0.5rem' }}>{statusSlot}</div>}
      {bar}
    </div>
  );
}
