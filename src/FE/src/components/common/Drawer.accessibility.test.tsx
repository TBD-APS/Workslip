import { useState } from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, describe, expect, it } from 'vitest';
import { ConfirmDialog } from './ConfirmDialog';
import { Drawer } from './Drawer';

function AccessibilityHarness() {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);

  return (
    <>
      <button type="button" onClick={() => setDrawerOpen(true)}>Åbn historik</button>
      <Drawer isOpen={drawerOpen} onClose={() => setDrawerOpen(false)} title="Historik">
        <button type="button" onClick={() => setConfirmOpen(true)}>Åbn bekræftelse</button>
      </Drawer>
      <ConfirmDialog
        open={confirmOpen}
        title="Bekræft handling"
        message="Fortsæt med handlingen?"
        confirmLabel="Fortsæt"
        onConfirm={() => setConfirmOpen(false)}
        onClose={() => setConfirmOpen(false)}
      />
    </>
  );
}

function renderHarness() {
  const router = createMemoryRouter(
    [{ path: '/app', element: <AccessibilityHarness /> }],
    { initialEntries: ['/app'] },
  );
  render(<RouterProvider router={router} />);
}

afterEach(() => cleanup());

describe('Drawer accessibility', () => {
  it('moves focus into the drawer and restores it when Escape closes the drawer', async () => {
    renderHarness();

    const trigger = screen.getByRole('button', { name: 'Åbn historik' });
    trigger.focus();
    fireEvent.click(trigger);

    const drawer = screen.getByRole('dialog', { name: 'Historik' });
    expect(drawer).toHaveAttribute('aria-modal', 'true');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Tilbage fra historik' })).toHaveFocus());

    fireEvent.keyDown(document, { key: 'Escape' });

    await waitFor(() => expect(drawer).not.toHaveClass('open'));
    expect(trigger).toHaveFocus();
  });

  it('keeps keyboard focus contained inside the open drawer', async () => {
    renderHarness();
    fireEvent.click(screen.getByRole('button', { name: 'Åbn historik' }));

    const close = screen.getByRole('button', { name: 'Tilbage fra historik' });
    const nestedTrigger = screen.getByRole('button', { name: 'Åbn bekræftelse' });
    await waitFor(() => expect(close).toHaveFocus());

    nestedTrigger.focus();
    fireEvent.keyDown(document, { key: 'Tab' });
    expect(close).toHaveFocus();

    fireEvent.keyDown(document, { key: 'Tab', shiftKey: true });
    expect(nestedTrigger).toHaveFocus();
  });

  it('lets only the topmost nested dialog handle Escape', async () => {
    renderHarness();
    fireEvent.click(screen.getByRole('button', { name: 'Åbn historik' }));

    const nestedTrigger = screen.getByRole('button', { name: 'Åbn bekræftelse' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Tilbage fra historik' })).toHaveFocus());
    nestedTrigger.focus();
    fireEvent.click(nestedTrigger);
    await waitFor(() => expect(screen.getByRole('dialog', { name: 'Bekræft handling' })).toBeInTheDocument());

    fireEvent.keyDown(document, { key: 'Escape' });

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Bekræft handling' })).not.toBeInTheDocument());
    expect(screen.getByRole('dialog', { name: 'Historik' })).toHaveClass('open');
    expect(nestedTrigger).toHaveFocus();

    fireEvent.keyDown(document, { key: 'Escape' });
    await waitFor(() => expect(screen.getByRole('dialog', { name: 'Historik' })).not.toHaveClass('open'));
  });
});
