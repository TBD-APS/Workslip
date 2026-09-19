import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ConfirmActionDialog } from './ConfirmActionDialog';

function doubleActionLabels() {
  const actions = screen.getByRole('dialog').querySelector('.modal-actions--double');
  expect(actions).not.toBeNull();
  return Array.from(actions!.querySelectorAll('button')).map((button) => button.textContent);
}

describe('ConfirmActionDialog', () => {
  it('places the approve action after cancel', () => {
    render(
      <ConfirmActionDialog
        action="approve"
        reportNumber="WS-271"
        isPending={false}
        onConfirm={vi.fn()}
        onClose={vi.fn()}
      />,
    );

    expect(doubleActionLabels()).toEqual(['Annuller', 'Godkend']);
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

  it('places the reject action after cancel, in the same position as approve', () => {
    render(
      <ConfirmActionDialog
        action="reject"
        reportNumber="WS-271"
        isPending={false}
        onConfirm={vi.fn()}
        onClose={vi.fn()}
      />,
    );

    expect(doubleActionLabels()).toEqual(['Annuller', 'Afvis']);
  });
});
