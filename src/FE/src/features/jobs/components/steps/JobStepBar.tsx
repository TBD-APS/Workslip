import { useEffect, useRef } from 'react';
import { Lock } from 'lucide-react';
import { JOB_STEPS } from './jobSteps';
import './JobStepBar.css';

export type StepMeta = {
  /** Kort status vist som tooltip, fx "3 anlæg valgt · gemt 14:12" */
  summary?: string;
  /** Hvorfor trinnet er låst. Sættes kun på låste trin. */
  lockedReason?: string;
  /** Antal manglende, påkrævede felter på trinnet. */
  missingCount?: number;
};

type JobStepBarProps = {
  currentStep: number;
  onStepChange: (step: number) => void;
  completedSteps: boolean[];
  /** Valgfri ekstra info pr. trin, indekseret som JOB_STEPS. */
  stepMeta?: StepMeta[];
};

const isLocked = (index: number, completedSteps: boolean[]) =>
  index > 0 && !completedSteps[index - 1];

export function JobStepBar({
  currentStep,
  onStepChange,
  completedSteps,
  stepMeta = [],
}: JobStepBarProps) {
  const trackRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const track = trackRef.current;
    if (!track) return;
    const activeSeg = track.querySelector<HTMLButtonElement>('button[aria-current="step"]');
    if (!activeSeg) return;

    const targetLeft = activeSeg.offsetLeft - (track.clientWidth - activeSeg.clientWidth) / 2;
    track.scrollTo({ left: targetLeft, behavior: 'smooth' });
  }, [currentStep]);

  return (
    <div className="job-step-bar" ref={trackRef} role="tablist" aria-label="Trin i sagen">
      {JOB_STEPS.map((step, index) => {
        const locked = isLocked(index, completedSteps);
        const done = index < currentStep;
        const current = index === currentStep;
        const meta = stepMeta[index];
        const missing = meta?.missingCount ?? 0;
        const tooltip = locked
          ? meta?.lockedReason ?? 'Låst, indtil de forrige trin er udfyldt.'
          : meta?.summary;
        const state = current ? 'current' : done ? 'done' : 'todo';

        return (
          <button
            key={step.label}
            type="button"
            role="tab"
            className={`job-step-seg is-${state}${locked ? ' is-locked' : ''}`}
            aria-current={current ? 'step' : undefined}
            aria-disabled={locked || undefined}
            aria-label={`Trin ${index + 1}: ${step.label}${locked ? ' — låst' : ''}`}
            title={tooltip}
            onClick={() => onStepChange(index)}
          >
            <span className="job-step-seg-line" />
            <span className="job-step-seg-label">
              {locked && <Lock size={11} className="job-step-seg-lock" />}
              <span className="job-step-seg-text">{step.label}</span>
              {current && missing > 0 && (
                <span className="job-step-seg-missing">{missing}</span>
              )}
            </span>
          </button>
        );
      })}
    </div>
  );
}
