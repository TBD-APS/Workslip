import { useCallback, useEffect, useRef, useState } from 'react';
import { Check, Lock } from 'lucide-react';
import { JOB_STEPS } from './jobSteps';
import './JobStepBar.css';

export type StepMeta = {
  /** Kort status vist i popoveren, fx "3 anlæg valgt · gemt 14:12" */
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
  /** Hvor længe der skal holdes, før popoveren vises. */
  holdDelayMs?: number;
};

const isLocked = (index: number, completedSteps: boolean[]) =>
  index > 0 && !completedSteps[index - 1];

export function JobStepBar({
  currentStep,
  onStepChange,
  completedSteps,
  stepMeta = [],
  holdDelayMs = 300,
}: JobStepBarProps) {
  const trackRef = useRef<HTMLDivElement | null>(null);
  const holdTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const dismissTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const didHold = useRef(false);
  const [peek, setPeek] = useState<number | null>(null);

  const clearTimers = useCallback(() => {
    clearTimeout(holdTimer.current);
    clearTimeout(dismissTimer.current);
  }, []);

  useEffect(() => clearTimers, [clearTimers]);

  const go = (index: number) => {
    if (isLocked(index, completedSteps)) {
      setPeek(index);
      clearTimeout(dismissTimer.current);
      dismissTimer.current = setTimeout(() => setPeek(null), 2600);
      return;
    }
    setPeek(null);
    onStepChange(index);
  };

  const onPointerDown = (index: number) => () => {
    didHold.current = false;
    clearTimers();
    holdTimer.current = setTimeout(() => {
      didHold.current = true;
      setPeek(index);
    }, holdDelayMs);
  };

  const onPointerUp = (index: number) => () => {
    clearTimeout(holdTimer.current);
    if (didHold.current && isLocked(index, completedSteps)) {
      dismissTimer.current = setTimeout(() => setPeek(null), 2600);
      return;
    }
    go(index);
  };

  const onPointerCancel = () => {
    clearTimeout(holdTimer.current);
    setPeek(null);
  };

  const peeked = peek === null ? null : JOB_STEPS[peek];
  const peekMeta = peek === null ? undefined : stepMeta[peek];
  const peekLocked = peek !== null && isLocked(peek, completedSteps);
  const peekDone = peek !== null && peek < currentStep;
  const current = JOB_STEPS[currentStep];
  const missingNow = stepMeta[currentStep]?.missingCount ?? 0;

  return (
    <div className="job-step-bar">
      <div className="job-step-bar-head">
        <span className="job-step-bar-title">
          Trin {currentStep + 1} af {JOB_STEPS.length} · <span>{current.label}</span>
        </span>
        {missingNow > 0 && (
          <span className="job-step-bar-missing">{missingNow} mangler</span>
        )}
      </div>

      <div className="job-step-bar-track" ref={trackRef} role="tablist" aria-label="Trin i sagen">
        {JOB_STEPS.map((step, index) => {
          const locked = isLocked(index, completedSteps);
          const state =
            index === currentStep ? 'current' : index < currentStep ? 'done' : 'todo';
          return (
            <button
              key={step.label}
              type="button"
              role="tab"
              className={`job-step-seg is-${state}${peek === index ? ' is-peeked' : ''}`}
              aria-current={index === currentStep ? 'step' : undefined}
              aria-disabled={locked || undefined}
              aria-label={`Trin ${index + 1}: ${step.label}${locked ? ' — låst' : ''}`}
              onPointerDown={onPointerDown(index)}
              onPointerUp={onPointerUp(index)}
              onPointerLeave={onPointerCancel}
              onPointerCancel={onPointerCancel}
              onContextMenu={(e) => e.preventDefault()}
            >
              <span className="job-step-seg-line" />
            </button>
          );
        })}
      </div>

      {peeked && (
        <div
          className="job-step-peek"
          role="status"
          style={{
            // Holder popoveren inden for bjælken: venstrestillet i første halvdel,
            // højrestillet i anden halvdel.
            [(peek as number) < JOB_STEPS.length / 2 ? 'left' : 'right']: 0,
          }}
        >
          <div className="job-step-peek-head">
            {peekLocked ? (
              <Lock size={14} />
            ) : peekDone ? (
              <Check size={14} className="job-step-peek-check" />
            ) : null}
            <span>
              Trin {(peek as number) + 1} · {peeked.label}
            </span>
          </div>
          <p className="job-step-peek-body">
            {peekLocked
              ? peekMeta?.lockedReason ?? 'Låst, indtil de forrige trin er udfyldt.'
              : peekMeta?.summary ?? (peekDone ? 'Færdig' : 'Ikke udfyldt endnu')}
          </p>
          <p className="job-step-peek-hint">
            {peekLocked ? 'Slip for at se hvad der mangler' : 'Slip for at åbne trinnet'}
          </p>
        </div>
      )}
    </div>
  );
}
