import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AppScrollRestoreBoundary } from './useAppRouteScroll';
import { usePaginatedList } from './usePaginatedList';

const queryState = vi.hoisted(() => ({ isLoading: true }));

vi.mock('./useInfiniteList', () => ({
  useInfiniteList: () => ({
    items: [],
    totalCount: 0,
    isLoading: queryState.isLoading,
    isFetching: false,
    isError: false,
    isFetchingNextPage: false,
    hasNextPage: false,
    fetchNextPage: vi.fn(),
    refetch: vi.fn(),
  }),
}));

vi.mock('./useInfiniteScroll', () => ({
  useInfiniteScroll: () => ({ sentinelRef: vi.fn() }),
}));

vi.mock('./useMediaQuery', () => ({
  useMediaQuery: () => false,
}));

const scrollTo = vi.fn(function scrollTo(this: HTMLElement, options: ScrollToOptions) {
  this.scrollTop = options.top ?? this.scrollTop;
});

function PaginatedListConsumer() {
  usePaginatedList({
    queryKey: ['items'],
    storageKey: 'items',
    fetchPage: async () => ({ items: [], totalCount: 0 }),
  });
  return null;
}

function Harness({ restoreKey = 'pop-entry' }: { restoreKey?: string | null }) {
  return (
    <AppScrollRestoreBoundary restoreKey={restoreKey}>
      <div className="app-shell">
        <PaginatedListConsumer />
      </div>
    </AppScrollRestoreBoundary>
  );
}

beforeEach(() => {
  queryState.isLoading = true;
  sessionStorage.clear();
  vi.useFakeTimers();
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
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('usePaginatedList scroll restoration', () => {
  it('protects a saved POP position while loading and flushes a later scroll on exit', () => {
    sessionStorage.setItem('items:scroll', '240');
    const rendered = render(<Harness />);
    const shell = rendered.container.querySelector<HTMLElement>('.app-shell')!;

    shell.scrollTop = 0;
    shell.dispatchEvent(new Event('scroll'));
    act(() => vi.advanceTimersByTime(250));
    expect(sessionStorage.getItem('items:scroll')).toBe('240');

    queryState.isLoading = false;
    rendered.rerender(<Harness />);
    expect(scrollTo).toHaveBeenLastCalledWith({ top: 240 });

    shell.scrollTop = 310;
    shell.dispatchEvent(new Event('scroll'));
    rendered.unmount();
    expect(sessionStorage.getItem('items:scroll')).toBe('310');
  });

  it('does not let an already-scheduled save overwrite a newly pending POP restore', () => {
    queryState.isLoading = false;
    sessionStorage.setItem('items:scroll', '240');
    const rendered = render(<Harness restoreKey={null} />);
    const shell = rendered.container.querySelector<HTMLElement>('.app-shell')!;

    shell.scrollTop = 120;
    shell.dispatchEvent(new Event('scroll'));

    queryState.isLoading = true;
    rendered.rerender(<Harness restoreKey="pop-entry" />);
    act(() => vi.advanceTimersByTime(250));

    expect(sessionStorage.getItem('items:scroll')).toBe('240');
  });
});
