import type { PropsWithChildren } from 'react';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { describe, expect, it, vi } from 'vitest';
import { useInfiniteList } from './useInfiniteList';

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return function Wrapper({ children }: PropsWithChildren) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  };
}

describe('useInfiniteList refresh', () => {
  it('discards later pages and refetches only the first page', async () => {
    const fetchPage = vi.fn(async ({ offset }: { limit: number; offset: number }) => {
      if (offset === 0) return { items: ['job-1', 'job-2'], totalCount: 4 };
      return { items: ['job-3', 'job-4'], totalCount: 4 };
    });

    const { result } = renderHook(
      () => useInfiniteList({ queryKey: ['jobs'], fetchPage, pageSize: 2 }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.items).toEqual(['job-1', 'job-2']));

    await act(async () => {
      await result.current.fetchNextPage();
    });
    await waitFor(() => expect(result.current.items).toEqual(['job-1', 'job-2', 'job-3', 'job-4']));

    fetchPage.mockClear();

    await act(async () => {
      await result.current.refetch();
    });

    expect(fetchPage).toHaveBeenCalledTimes(1);
    expect(fetchPage).toHaveBeenCalledWith({ limit: 2, offset: 0 });
    await waitFor(() => expect(result.current.items).toEqual(['job-1', 'job-2']));
  });

  it('reuses the active refresh promise instead of starting a second request', async () => {
    let finishRefresh: ((value: { items: string[]; totalCount: number }) => void) | undefined;
    const fetchPage = vi
      .fn<({ limit, offset }: { limit: number; offset: number }) => Promise<{ items: string[]; totalCount: number }>>()
      .mockResolvedValueOnce({ items: ['job-1'], totalCount: 1 });

    const { result } = renderHook(
      () => useInfiniteList({ queryKey: ['jobs'], fetchPage, pageSize: 20 }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.items).toEqual(['job-1']));

    fetchPage.mockImplementationOnce(
      () => new Promise((resolve) => {
        finishRefresh = resolve;
      }),
    );
    fetchPage.mockClear();

    let firstRefresh: Promise<void> | undefined;
    let secondRefresh: Promise<void> | undefined;
    act(() => {
      firstRefresh = result.current.refetch();
      secondRefresh = result.current.refetch();
    });

    expect(firstRefresh).toBe(secondRefresh);
    expect(fetchPage).toHaveBeenCalledTimes(1);

    await act(async () => {
      finishRefresh?.({ items: ['job-1'], totalCount: 1 });
      await Promise.all([firstRefresh, secondRefresh]);
    });
  });
});
