import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ConfirmActionDialog } from './ConfirmActionDialog';

describe('ConfirmActionDialog reopen flow', () => {
  it('keeps reopen disabled until a reason is provided and returns the reason', () => {
    const onConfirm = vi.fn();

    render(
      <ConfirmActionDialog
        action="reopen"
        reportNumber="SAG-123"
        isPending={false}
        onConfirm={onConfirm}
        onClose={vi.fn()}
      />,
    );

    expect(screen.getByText(/godkendt og låst/i)).toBeInTheDocument();
    const reopenButton = screen.getByRole('button', { name: 'Genåbn sag' });
    expect(reopenButton).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Hvorfor skal sagen genåbnes?'), {
      target: { value: 'Dokumentationen skal rettes' },
    });

    expect(reopenButton).toBeEnabled();
    fireEvent.click(reopenButton);

    expect(onConfirm).toHaveBeenCalledWith('Dokumentationen skal rettes');
  });
});
