import { useEffect, useRef, useState, type ReactNode } from 'react';
import { CheckCircle2, ChevronLeft, ChevronRight } from 'lucide-react';
import { JOB_STEPS } from './jobSteps';

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
        const isDisabled = index === 3 && !completedSteps[2]; // Disable Worksheets if ControlPoints not valid
        return (
          <button
            key={step.label}
            className={`step-dot ${isActive ? 'active' : ''} ${isCompleted ? 'completed' : ''}`}
            onClick={() => !isDisabled && onStepChange(index)}
            disabled={isDisabled}
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
  const [isVisible, setIsVisible] = useState(true);

  useEffect(() => {
    const scrollContainer = document.querySelector('.app-content');
    const getScrollTop = () => scrollContainer?.scrollTop ?? window.scrollY;
    const getScrollBottom = () => {
      if (scrollContainer) {
        return scrollContainer.scrollHeight - scrollContainer.scrollTop - scrollContainer.clientHeight;
      }

      const documentElement = document.documentElement;
      return documentElement.scrollHeight - window.scrollY - window.innerHeight;
    };
    let lastScrollTop = getScrollTop();

    const handleScroll = () => {
      const currentScrollTop = getScrollTop();
      const isAtBottom = getScrollBottom() <= 24;
      const scrollingDown = currentScrollTop > lastScrollTop && currentScrollTop > 80;
      setIsVisible(isAtBottom || !scrollingDown);
      lastScrollTop = currentScrollTop;
    };

    const target = scrollContainer ?? window;
    target.addEventListener('scroll', handleScroll, { passive: true });
    return () => target.removeEventListener('scroll', handleScroll);
  }, []);

  return (
    <div className={isVisible ? 'step-nav-sticky' : 'step-nav-sticky hidden'}>
      {statusSlot && <div className="step-nav-status-slot">{statusSlot}</div>}
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
        ) : null}
      </div>
    </div>
  );
}
