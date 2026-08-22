import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { HelpWizard } from './HelpWizard';

describe('HelpWizard', () => {
  afterEach(() => localStorage.clear());

  it('renders nothing on the off-path', () => {
    render(<HelpWizard />);
    expect(screen.queryByTestId('help-wizard')).toBeNull();
  });

  it('opens and closes when the identity assignment enables it', () => {
    localStorage.setItem('workslip.flag.help-wizard', 'on');
    render(<HelpWizard />);

    const toggle = screen.getByRole('button', { name: 'Hjælp' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');

    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('status')).toBeInTheDocument();

    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('status')).toBeNull();
  });
});
