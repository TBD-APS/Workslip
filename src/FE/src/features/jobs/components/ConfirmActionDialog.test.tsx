import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ConfirmActionDialog } from './ConfirmActionDialog';

describe('ConfirmActionDialog', () => {
  it('places the blue approve action to the right of cancel', () => {
    render(
      <ConfirmActionDialog
        action="approve"
        reportNumber="WS-271"
        isPending={false}
        onConfirm={vi.fn()}
        onClose={vi.fn()}
      />,
    );

    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual([
      'Annuller',
      'Godkend',
    ]);
  });

  it('offers withdraw from review without requiring a reason', () => {
    const onConfirm = vi.fn();
    render(
      <ConfirmActionDialog
        action="withdraw"
        reportNumber="WS-271"
        isPending={false}
        onConfirm={onConfirm}
        onClose={vi.fn()}
      />,
    );

    expect(screen.getByRole('dialog', { name: 'Træk sag tilbage fra gennemsyn' })).toBeInTheDocument();
    expect(screen.queryByLabelText(/begrundelse|hvorfor/i)).not.toBeInTheDocument();

    const confirm = document.getElementById('job-report-withdraw-review-confirm');
    expect(confirm).not.toBeNull();
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm!);
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('places the reject action to the left of cancel', () => {
    render(
      <ConfirmActionDialog
        action="reject"
        reportNumber="WS-271"
        isPending={false}
        onConfirm={vi.fn()}
        onClose={vi.fn()}
      />,
    );

    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual([
      'Afvis',
      'Annuller',
    ]);
  });
});
