import { useState } from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmDeleteDialog } from './ConfirmDeleteDialog';

function DialogHarness() {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>Åbn sletning</button>
      <ConfirmDeleteDialog
        open={open}
        title="Slet sag"
        message="Handlingen kan ikke fortrydes."
        onConfirm={vi.fn()}
        onClose={() => setOpen(false)}
      />
    </>
  );
}

afterEach(() => {
  cleanup();
});

describe('ConfirmDeleteDialog accessibility', () => {
  it('moves focus to the safe action and exposes modal semantics', async () => {
    render(<DialogHarness />);

    const trigger = screen.getByRole('button', { name: 'Åbn sletning' });
    trigger.focus();
    fireEvent.click(trigger);

    const dialog = screen.getByRole('dialog', { name: 'Slet sag' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Annuller' })).toHaveFocus());
  });

  it('wraps keyboard focus inside the dialog', async () => {
    render(<DialogHarness />);
    fireEvent.click(screen.getByRole('button', { name: 'Åbn sletning' }));

    const cancel = screen.getByRole('button', { name: 'Annuller' });
    const confirm = screen.getByRole('button', { name: 'Slet' });
    await waitFor(() => expect(cancel).toHaveFocus());

    fireEvent.keyDown(document, { key: 'Tab' });
    expect(confirm).toHaveFocus();

    fireEvent.keyDown(document, { key: 'Tab', shiftKey: true });
    expect(cancel).toHaveFocus();
  });

  it('closes on Escape and restores focus to the invoking control', async () => {
    render(<DialogHarness />);

    const trigger = screen.getByRole('button', { name: 'Åbn sletning' });
    trigger.focus();
    fireEvent.click(trigger);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Annuller' })).toHaveFocus());

    fireEvent.keyDown(document, { key: 'Escape' });

    expect(screen.queryByRole('dialog', { name: 'Slet sag' })).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });
});
