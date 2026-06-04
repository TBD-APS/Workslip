import type { ReactNode } from 'react';
import { Building2, CheckCircle2, ChevronLeft, ChevronRight, ClipboardList, FileText, MessageSquare } from 'lucide-react';

export const JOB_STEPS = [
  { icon: Building2, label: 'Sagsdetaljer' },
  { icon: FileText, label: 'Kategorier' },
  { icon: ClipboardList, label: 'Kontrolpunkter' },
  { icon: MessageSquare, label: 'Bilag' },
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
  disableDone = false,
}: StepNavigationProps) {
  return (
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
        <button className="step-nav-btn step-nav-btn-next" onClick={onNext}>
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
