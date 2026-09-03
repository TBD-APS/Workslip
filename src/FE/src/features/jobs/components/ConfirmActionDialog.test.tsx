import { render, screen } from '@testing-library/react';
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
