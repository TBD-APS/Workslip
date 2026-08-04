import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { FullscreenSystemState } from './FullscreenSystemState';

describe('FullscreenSystemState', () => {
  it('renders Workslip identity and an accessible loading status', () => {
    render(
      <FullscreenSystemState
        title="Tjekker login"
        message="Vi kontrollerer din session."
      />,
    );

    expect(screen.getByText('Workslip')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Tjekker login' })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveAttribute('aria-busy', 'true');
  });

  it('renders recovery actions without marking the state as busy', () => {
    render(
      <FullscreenSystemState
        title="Forbindelsen tager længere tid end normalt"
        message="Dit gemte login er ikke blevet slettet."
        isLoading={false}
        actions={<button type="button">Prøv igen</button>}
        role="alert"
      />,
    );

    expect(screen.getByRole('alert')).toHaveAttribute('aria-busy', 'false');
    expect(screen.getByRole('button', { name: 'Prøv igen' })).toBeInTheDocument();
  });
});
