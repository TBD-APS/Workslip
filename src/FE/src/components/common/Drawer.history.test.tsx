import { useState } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider, useNavigate } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { Drawer } from './Drawer';

function DrawerHarness({ onClose }: { onClose: () => void }) {
  const [isOpen, setIsOpen] = useState(true);

  return (
    <Drawer
      isOpen={isOpen}
      onClose={() => {
        onClose();
        setIsOpen(false);
      }}
      title="Historik"
    >
      Indhold
    </Drawer>
  );
}

function PushNavigationHarness() {
  const navigate = useNavigate();

  return (
    <Drawer isOpen onClose={() => {}} title="Historik">
      <button type="button" onClick={() => navigate('/next')}>
        Åbn næste side
      </button>
    </Drawer>
  );
}

describe('Drawer browser history handling', () => {
  it('closes an open drawer and cancels browser back navigation', async () => {
    const onClose = vi.fn();
    const router = createMemoryRouter(
      [
        { path: '/login', element: <div>Login</div> },
        { path: '/app', element: <DrawerHarness onClose={onClose} /> },
      ],
      {
        initialEntries: ['/login', '/app'],
        initialIndex: 1,
      },
    );

    render(<RouterProvider router={router} />);

    await act(async () => {
      await router.navigate(-1);
    });

    await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
    expect(router.state.location.pathname).toBe('/app');
    expect(screen.getByRole('dialog', { name: 'Historik' })).not.toHaveClass('open');
  });

  it('does not block ordinary forward navigation from drawer content', async () => {
    const router = createMemoryRouter([
      { path: '/app', element: <PushNavigationHarness /> },
      { path: '/next', element: <div>Næste side</div> },
    ], {
      initialEntries: ['/app'],
    });

    render(<RouterProvider router={router} />);

    fireEvent.click(screen.getByRole('button', { name: 'Åbn næste side' }));

    expect(await screen.findByText('Næste side')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/next');
  });
});
