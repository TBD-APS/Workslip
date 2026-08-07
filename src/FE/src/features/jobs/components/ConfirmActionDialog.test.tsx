import { render, screen } from '@testing-library/react';
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
