import { act, cleanup, render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { createMemoryRouter, Outlet, RouterProvider } from 'react-router-dom';
import { useLayoutEffect, useRef } from 'react';
import {
  AppScrollRestoreBoundary,
  useAppRouteScrollManager,
} from '../../hooks/useAppRouteScroll';

let outgoingScrollTop: number | null = null;

function CaptureOutgoingScroll() {
  useLayoutEffect(() => () => {
    outgoingScrollTop = document.querySelector<HTMLElement>('.app-shell')?.scrollTop ?? null;
  }, []);
  return <div>First</div>;
}

function ScrollShell() {
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const restoreKey = useAppRouteScrollManager(scrollContainerRef);

  return (
    <AppScrollRestoreBoundary restoreKey={restoreKey}>
      <div ref={scrollContainerRef} className="app-shell">
        <Outlet />
      </div>
    </AppScrollRestoreBoundary>
  );
}

function MissingScrollShell() {
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const restoreKey = useAppRouteScrollManager(scrollContainerRef);
  return (
    <AppScrollRestoreBoundary restoreKey={restoreKey}>
      <Outlet />
    </AppScrollRestoreBoundary>
  );
}

function createTestRouter(element: React.ReactNode = <ScrollShell />) {
  return createMemoryRouter([
    {
      path: '/',
      element,
      children: [
        { path: 'first', element: <CaptureOutgoingScroll /> },
        { path: 'second', element: <div id="section">Second</div> },
      ],
    },
  ], { initialEntries: ['/first'] });
}

afterEach(() => {
  outgoingScrollTop = null;
  cleanup();
});

describe('AppLayout route scrolling', () => {
  it('resets initial, PUSH, REPLACE, and unrestored POP entries but preserves outgoing scroll for cleanup', async () => {
    const router = createTestRouter();
    const { container } = render(<RouterProvider router={router} />);
    const shell = container.querySelector<HTMLElement>('.app-shell')!;

    expect(shell.scrollTop).toBe(0);
    shell.scrollTop = 240;

    await act(async () => {
      await router.navigate('/second');
    });
    expect(outgoingScrollTop).toBe(240);
    expect(shell.scrollTop).toBe(0);

    shell.scrollTop = 180;
    await act(async () => {
      await router.navigate('/second', { replace: true });
    });
    expect(shell.scrollTop).toBe(0);

    shell.scrollTop = 160;
    await act(async () => {
      await router.navigate(-1);
    });
    expect(shell.scrollTop).toBe(0);
  });

  it('leaves hash positioning alone', async () => {
    const router = createTestRouter();
    const { container } = render(<RouterProvider router={router} />);
    const shell = container.querySelector<HTMLElement>('.app-shell')!;
    shell.scrollTop = 140;

    await act(async () => {
      await router.navigate('/second#section');
    });

    expect(shell.scrollTop).toBe(140);
  });

  it('is a safe no-op when the scroll container is not mounted', async () => {
    const router = createTestRouter(<MissingScrollShell />);
    render(<RouterProvider router={router} />);

    await expect(act(async () => {
      await router.navigate('/second');
    })).resolves.toBeUndefined();
  });
});
