import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createMemoryRouter, Outlet, RouterProvider, useParams } from 'react-router-dom';
import { useRef } from 'react';
import {
  AppScrollRestoreBoundary,
  useAppRouteScrollManager,
} from './useAppRouteScroll';
import { useScrollRestore } from './useScrollRestore';

const scrollTo = vi.fn(function scrollTo(this: HTMLElement, options: ScrollToOptions) {
  this.scrollTop = options.top ?? this.scrollTop;
});

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

function DetailRoute() {
  const { id } = useParams<{ id: string }>();
  useScrollRestore(`detail:${id}`);
  return <div>{id}</div>;
}

function createTestRouter(initialEntry: string) {
  return createMemoryRouter([
    {
      path: '/',
      element: <ScrollShell />,
      children: [{ path: 'detail/:id', element: <DetailRoute /> }],
    },
  ], { initialEntries: [initialEntry] });
}

beforeEach(() => {
  sessionStorage.clear();
  scrollTo.mockClear();
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', {
    configurable: true,
    value: scrollTo,
  });
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
    callback(0);
    return 1;
  });
  vi.stubGlobal('cancelAnimationFrame', vi.fn());
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('useScrollRestore', () => {
  it('starts a direct load and PUSH at the top, then restores on POP', async () => {
    sessionStorage.setItem('scroll:detail:first', '180');
    sessionStorage.setItem('scroll:detail:second', '320');
    const router = createTestRouter('/detail/first');
    const { container } = render(<RouterProvider router={router} />);
    const shell = container.querySelector<HTMLElement>('.app-shell')!;

    expect(scrollTo).not.toHaveBeenCalled();
    expect(shell.scrollTop).toBe(0);
    shell.scrollTop = 180;
    shell.dispatchEvent(new Event('scroll'));

    await act(async () => {
      await router.navigate('/detail/second');
    });
    expect(scrollTo).not.toHaveBeenCalled();
    expect(shell.scrollTop).toBe(0);

    shell.scrollTop = 320;
    shell.dispatchEvent(new Event('scroll'));
    await act(async () => {
      await router.navigate(-1);
    });
    expect(sessionStorage.getItem('scroll:detail:second')).toBe('320');
    expect(scrollTo).toHaveBeenLastCalledWith({ top: 180 });
  });

  it('restores on consecutive POP entries even when the semantic storage key is unchanged', async () => {
    sessionStorage.setItem('scroll:detail:first', '180');
    const router = createTestRouter('/detail/first?step=1');
    const { container } = render(<RouterProvider router={router} />);
    const shell = container.querySelector<HTMLElement>('.app-shell')!;
    shell.scrollTop = 180;
    shell.dispatchEvent(new Event('scroll'));

    await act(async () => {
      await router.navigate('/detail/first?step=2');
      await router.navigate('/detail/first?step=3');
    });

    scrollTo.mockClear();
    await act(async () => {
      await router.navigate(-1);
    });
    expect(scrollTo).toHaveBeenLastCalledWith({ top: 180 });

    sessionStorage.setItem('scroll:detail:first', '220');
    scrollTo.mockClear();
    await act(async () => {
      await router.navigate(-1);
    });
    expect(scrollTo).toHaveBeenLastCalledWith({ top: 220 });
  });

  it('does not overwrite hash positioning with saved scroll', async () => {
    sessionStorage.setItem('scroll:detail:first', '180');
    const router = createTestRouter('/detail/first');
    const { container } = render(<RouterProvider router={router} />);
    const shell = container.querySelector<HTMLElement>('.app-shell')!;
    shell.scrollTop = 90;

    await act(async () => {
      await router.navigate('/detail/first#section');
    });

    expect(shell.scrollTop).toBe(90);
    expect(scrollTo).not.toHaveBeenCalled();
  });

  it('ignores invalid saved positions on POP', async () => {
    const router = createTestRouter('/detail/first');
    render(<RouterProvider router={router} />);

    await act(async () => {
      await router.navigate('/detail/second');
    });
    sessionStorage.setItem('scroll:detail:first', 'not-a-number');
    await act(async () => {
      await router.navigate(-1);
    });

    expect(scrollTo).not.toHaveBeenCalled();
  });
});
