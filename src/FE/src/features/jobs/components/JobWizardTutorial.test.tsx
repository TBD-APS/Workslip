import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { JobWizardTutorial } from './JobWizardTutorial';

const GUIDE_SEEN_STORAGE_KEY = 'workslip.job-wizard-guide-seen.v1';

describe('JobWizardTutorial', () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it('opens on first use, follows the real wizard step, and remembers dismissal', () => {
    const { rerender, unmount } = render(<JobWizardTutorial currentStep={0} />);

    expect(screen.getByText('Start med sagens grundoplysninger')).toBeInTheDocument();
    expect(screen.getByText('Guide · trin 1 af 6')).toBeInTheDocument();

    rerender(<JobWizardTutorial currentStep={3} />);

    expect(screen.getByText('Registrér arbejdstid og eventuelle udlæg')).toBeInTheDocument();
    expect(screen.getByText('Guide · trin 4 af 6')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Luk guide' }));

    expect(screen.queryByText('Registrér arbejdstid og eventuelle udlæg')).not.toBeInTheDocument();
    expect(window.localStorage.getItem(GUIDE_SEEN_STORAGE_KEY)).toBe('1');

    unmount();
    render(<JobWizardTutorial currentStep={0} />);

    expect(screen.queryByText('Start med sagens grundoplysninger')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guide til dette trin' })).toBeInTheDocument();
  });
});
