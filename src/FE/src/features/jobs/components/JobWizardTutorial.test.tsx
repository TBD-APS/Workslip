import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { JobWizardTutorial } from './JobWizardTutorial';

const authState = vi.hoisted(() => ({
  user: {
    id: 'user-1',
    organizationId: 'org-1',
  },
}));

vi.mock('../../../providers/useAuth', () => ({
  useAuth: () => ({ user: authState.user }),
}));

const guideSeenStorageKey = (organizationId: string, userId: string) =>
  `workslip.job-wizard-guide-seen.v2.${organizationId}.${userId}`;

describe('JobWizardTutorial', () => {
  beforeEach(() => {
    window.localStorage.clear();
    authState.user = {
      id: 'user-1',
      organizationId: 'org-1',
    };
  });

  it('opens on first use, follows the real wizard step, and remembers dismissal for the current user', () => {
    const { rerender, unmount } = render(<JobWizardTutorial currentStep={0} />);

    expect(screen.getByText('Start med de vigtigste oplysninger')).toBeInTheDocument();
    expect(screen.getByText('Trin 1 af 6')).toBeInTheDocument();

    rerender(<JobWizardTutorial currentStep={3} />);

    expect(screen.getByText('Registrér tid og eventuelle udlæg')).toBeInTheDocument();
    expect(screen.getByText('Trin 4 af 6')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Skjul hjælp' }));

    expect(screen.queryByText('Registrér tid og eventuelle udlæg')).not.toBeInTheDocument();
    expect(window.localStorage.getItem(guideSeenStorageKey('org-1', 'user-1'))).toBe('1');

    unmount();
    render(<JobWizardTutorial currentStep={0} />);

    expect(screen.queryByText('Start med de vigtigste oplysninger')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Vis hjælp' })).toBeInTheDocument();
  });

  it('does not let one user suppress first-use guidance for another user or organization', () => {
    const { unmount } = render(<JobWizardTutorial currentStep={0} />);
    fireEvent.click(screen.getByRole('button', { name: 'Skjul hjælp' }));
    unmount();

    authState.user = {
      id: 'user-2',
      organizationId: 'org-1',
    };
    const secondUser = render(<JobWizardTutorial currentStep={0} />);
    expect(screen.getByText('Start med de vigtigste oplysninger')).toBeInTheDocument();
    secondUser.unmount();

    authState.user = {
      id: 'user-1',
      organizationId: 'org-2',
    };
    render(<JobWizardTutorial currentStep={0} />);
    expect(screen.getByText('Start med de vigtigste oplysninger')).toBeInTheDocument();
  });

  it('describes the actual irrelevant-category behavior on the control-point step', () => {
    render(<JobWizardTutorial currentStep={2} />);

    expect(screen.getByText(/Vælg mindst ét kontrolpunkt i hver relevant kategori/)).toBeInTheDocument();
    expect(screen.getByText(/Hvis en kategori ikke gælder for arbejdet, kan du markere den som ikke relevant/)).toBeInTheDocument();
    expect(screen.getByText(/Hvis alle valgte kategorier er irrelevante, kan du tilføje en samlet forklaring/)).toBeInTheDocument();
    expect(screen.getByText(/tjek om der mangler et valg eller en forklaring på dette trin/)).toBeInTheDocument();
    expect(screen.queryByText(/krævede begrundelse/)).not.toBeInTheDocument();
  });
});
