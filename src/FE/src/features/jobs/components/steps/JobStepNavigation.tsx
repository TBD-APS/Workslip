import { useEffect, useRef, useState, type ReactNode } from 'react';
import { CheckCircle2, ChevronLeft, ChevronRight } from 'lucide-react';
import { JOB_STEPS } from './jobSteps';

type StepIndicatorsProps = {
  currentStep: number;
  onStepChange: (step: number) => void;
};

export function StepIndicators({ currentStep, onStepChange }: StepIndicatorsProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;
    const activeDot = container.querySelector<HTMLButtonElement>('button[aria-current="step"]');
    activeDot?.scrollIntoView({ inline: 'center', block: 'nearest' });
  }, [currentStep]);

  return (
    <div className="step-indicators" ref={containerRef}>
      {JOB_STEPS.map((step, index) => {
        const StepIcon = step.icon;
        const isActive = index === currentStep;
        const isCompleted = index < currentStep;
        return (
          <button
            key={step.label}
            className={`step-dot ${isActive ? 'active' : ''} ${isCompleted ? 'completed' : ''}`}
            onClick={() => onStepChange(index)}
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
    <div className={isVisible ? 'step-nav step-nav-sticky' : 'step-nav step-nav-sticky hidden'}>
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
  );
}
