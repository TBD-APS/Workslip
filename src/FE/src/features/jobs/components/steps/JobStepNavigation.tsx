import { useEffect, useState, type ReactNode } from 'react';
import { Building2, CheckCircle2, ChevronLeft, ChevronRight, ClipboardList, FileSpreadsheet, FileText } from 'lucide-react';

export const JOB_STEPS = [
  { icon: Building2, label: 'Sagsdetaljer' },
  { icon: FileText, label: 'Kategorier' },
  { icon: ClipboardList, label: 'Kontrolpunkter' },
  { icon: FileSpreadsheet, label: 'Arbejdssedler' },
] as const;

type StepIndicatorsProps = {
  currentStep: number;
  onStepChange: (step: number) => void;
};

export function StepIndicators({ currentStep, onStepChange }: StepIndicatorsProps) {
  return (
    <div className="step-indicators">
      {JOB_STEPS.map((step, index) => {
        const StepIcon = step.icon;
        const isActive = index === currentStep;
        const isCompleted = index < currentStep;
        return (
          <button
            key={step.label}
            className={`step-dot ${isActive ? 'active' : ''} ${isCompleted ? 'completed' : ''}`}
            onClick={() => onStepChange(index)}
            aria-label={step.label}
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
        <button className="step-nav-btn step-nav-btn-next" onClick={onNext} disabled={disableNext}>
          <span>Næste</span>
          <ChevronRight size={18} />
        </button>
      ) : (
        <button className="step-nav-btn step-nav-btn-next" onClick={onDone} disabled={disableDone}>
          {doneIcon}
          <span>{doneLabel}</span>
        </button>
      )}
    </div>
  );
}
