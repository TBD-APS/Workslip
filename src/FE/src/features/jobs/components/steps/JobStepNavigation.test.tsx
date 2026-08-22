import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { StepNavigation } from './JobStepNavigation';

vi.mock('../../../../providers/DropdownContext', () => ({
  useDropdownContext: () => ({ openDropdowns: 0 }),
}));

vi.mock('../JobWizardTutorial', () => ({
  JobWizardTutorial: () => null,
}));

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
    expect(next).toHaveAttribute('aria-disabled', 'true');
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
