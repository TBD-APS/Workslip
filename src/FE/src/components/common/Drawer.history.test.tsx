import { useState } from 'react';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider, useNavigate } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Drawer } from './Drawer';

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

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

function renderOpenDrawer(onClose: () => void) {
  const router = createMemoryRouter(
    [{ path: '/app', element: <DrawerHarness onClose={onClose} /> }],
    { initialEntries: ['/app'] },
  );

  render(<RouterProvider router={router} />);
  const drawer = screen.getByRole('dialog', { name: 'Historik' });
  vi.spyOn(drawer, 'getBoundingClientRect').mockReturnValue({
    x: 0,
    y: 0,
    top: 0,
    right: 390,
    bottom: 844,
    left: 0,
    width: 390,
    height: 844,
    toJSON: () => ({}),
  });

  return { drawer, router };
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
    const drawer = screen.getByRole('dialog', { name: 'Historik' });

    await act(async () => {
      await router.navigate(-1);
    });

    await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
    expect(router.state.location.pathname).toBe('/app');
    expect(drawer).not.toHaveClass('open');
    expect(drawer).toHaveAttribute('aria-hidden', 'true');
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

  it('returns to open after an incomplete viewport-edge swipe', () => {
    const onClose = vi.fn();
    const { drawer, router } = renderOpenDrawer(onClose);

    fireEvent.touchStart(window, {
      cancelable: true,
      touches: [{ identifier: 1, clientX: 8, clientY: 120 }],
    });
    fireEvent.touchMove(window, {
      cancelable: true,
      touches: [{ identifier: 1, clientX: 42, clientY: 120 }],
    });
    fireEvent.touchEnd(window, {
      cancelable: true,
      touches: [],
      changedTouches: [{ identifier: 1, clientX: 42, clientY: 120 }],
    });

    expect(onClose).not.toHaveBeenCalled();
    expect(router.state.location.pathname).toBe('/app');
    expect(drawer).toHaveClass('open');
  });

  it('closes locally when a rightward swipe starts at the viewport edge', () => {
    const onClose = vi.fn();
    const { drawer, router } = renderOpenDrawer(onClose);

    fireEvent.touchStart(window, {
      cancelable: true,
      touches: [{ identifier: 2, clientX: 8, clientY: 120 }],
    });
    fireEvent.touchMove(window, {
      cancelable: true,
      touches: [{ identifier: 2, clientX: 130, clientY: 120 }],
    });
    fireEvent.touchEnd(window, {
      cancelable: true,
      touches: [],
      changedTouches: [{ identifier: 2, clientX: 130, clientY: 120 }],
    });

    expect(onClose).toHaveBeenCalledOnce();
    expect(router.state.location.pathname).toBe('/app');
    expect(drawer).not.toHaveClass('open');
  });

  it('does not hijack touches that start away from the viewport edge', () => {
    const onClose = vi.fn();
    const { drawer } = renderOpenDrawer(onClose);

    fireEvent.touchStart(window, {
      cancelable: true,
      touches: [{ identifier: 3, clientX: 60, clientY: 120 }],
    });
    fireEvent.touchMove(window, {
      cancelable: true,
      touches: [{ identifier: 3, clientX: 220, clientY: 120 }],
    });
    fireEvent.touchEnd(window, {
      cancelable: true,
      touches: [],
      changedTouches: [{ identifier: 3, clientX: 220, clientY: 120 }],
    });

    expect(onClose).not.toHaveBeenCalled();
    expect(drawer).toHaveClass('open');
  });
});
