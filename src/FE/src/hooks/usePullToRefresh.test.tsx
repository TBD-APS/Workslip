import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { usePullToRefresh } from './usePullToRefresh';

type TouchPoint = {
  clientX: number;
  clientY: number;
};

function dispatchTouch(target: HTMLElement, type: string, touches: TouchPoint[]) {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'touches', { value: touches });
  target.dispatchEvent(event);
  return event;
}

function pull(target: HTMLElement, from: TouchPoint, to: TouchPoint) {
  dispatchTouch(target, 'touchstart', [from]);
  const moveEvent = dispatchTouch(target, 'touchmove', [to]);
  dispatchTouch(target, 'touchend', []);
  return moveEvent;
}

describe('usePullToRefresh', () => {
  let scrollContainer: HTMLDivElement;

  beforeEach(() => {
    scrollContainer = document.createElement('div');
    scrollContainer.className = 'app-shell';
    document.body.append(scrollContainer);
  });

  afterEach(() => {
    scrollContainer.remove();
  });

  it('refreshes after a downward pull crosses the threshold', async () => {
    const onRefresh = vi.fn().mockResolvedValue(undefined);
    const { result } = renderHook(() => usePullToRefresh({ onRefresh }));

    act(() => {
      const moveEvent = pull(
        scrollContainer,
        { clientX: 20, clientY: 10 },
        { clientX: 20, clientY: 150 },
      );
      expect(moveEvent.defaultPrevented).toBe(true);
    });

    await waitFor(() => expect(onRefresh).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(result.current.isRefreshing).toBe(false));
  });

  it('does not refresh after a short pull', () => {
    const onRefresh = vi.fn();
    renderHook(() => usePullToRefresh({ onRefresh }));

    act(() => {
      pull(
        scrollContainer,
        { clientX: 20, clientY: 10 },
        { clientX: 20, clientY: 80 },
      );
    });

    expect(onRefresh).not.toHaveBeenCalled();
  });

  it('preserves scrolled and horizontal gestures', () => {
    const onRefresh = vi.fn();
    renderHook(() => usePullToRefresh({ onRefresh }));

    scrollContainer.scrollTop = 10;
    act(() => {
      const scrolledMove = pull(
        scrollContainer,
        { clientX: 20, clientY: 10 },
        { clientX: 20, clientY: 180 },
      );
      expect(scrolledMove.defaultPrevented).toBe(false);
    });

    scrollContainer.scrollTop = 0;
    act(() => {
      dispatchTouch(scrollContainer, 'touchstart', [{ clientX: 10, clientY: 20 }]);
      const undecidedMove = dispatchTouch(scrollContainer, 'touchmove', [{ clientX: 10, clientY: 29 }]);
      const horizontalMove = dispatchTouch(scrollContainer, 'touchmove', [{ clientX: 160, clientY: 45 }]);
      dispatchTouch(scrollContainer, 'touchend', []);
      expect(undecidedMove.defaultPrevented).toBe(false);
      expect(horizontalMove.defaultPrevented).toBe(false);
    });

    expect(onRefresh).not.toHaveBeenCalled();
  });

  it('allows only one refresh at a time', async () => {
    let finishRefresh: (() => void) | undefined;
    const pendingRefresh = new Promise<void>((resolve) => {
      finishRefresh = resolve;
    });
    const onRefresh = vi.fn(() => pendingRefresh);
    const { result } = renderHook(() => usePullToRefresh({ onRefresh }));

    act(() => {
      pull(
        scrollContainer,
        { clientX: 20, clientY: 10 },
        { clientX: 20, clientY: 150 },
      );
    });

    await waitFor(() => expect(onRefresh).toHaveBeenCalledTimes(1));
    expect(result.current.isRefreshing).toBe(true);

    act(() => {
      pull(
        scrollContainer,
        { clientX: 20, clientY: 10 },
        { clientX: 20, clientY: 150 },
      );
    });

    expect(onRefresh).toHaveBeenCalledTimes(1);

    finishRefresh?.();
    await waitFor(() => expect(result.current.isRefreshing).toBe(false));
  });

  it('cancels a pull when another touch joins', () => {
    const onRefresh = vi.fn();
    renderHook(() => usePullToRefresh({ onRefresh }));

    act(() => {
      dispatchTouch(scrollContainer, 'touchstart', [{ clientX: 20, clientY: 10 }]);
      dispatchTouch(scrollContainer, 'touchmove', [{ clientX: 20, clientY: 150 }]);
      dispatchTouch(scrollContainer, 'touchstart', [
        { clientX: 20, clientY: 150 },
        { clientX: 60, clientY: 150 },
      ]);
      dispatchTouch(scrollContainer, 'touchend', [{ clientX: 20, clientY: 150 }]);
    });

    expect(onRefresh).not.toHaveBeenCalled();
  });

  it('recovers when the refresh callback throws synchronously', async () => {
    const onRefresh = vi.fn(() => {
      throw new Error('refresh failed');
    });
    const { result } = renderHook(() => usePullToRefresh({ onRefresh }));

    act(() => {
      pull(
        scrollContainer,
        { clientX: 20, clientY: 10 },
        { clientX: 20, clientY: 150 },
      );
    });

    await waitFor(() => expect(result.current.isRefreshing).toBe(false));
    expect(onRefresh).toHaveBeenCalledTimes(1);
  });

  it('ignores pulls inside an open overlay', () => {
    const onRefresh = vi.fn();
    const drawer = document.createElement('div');
    drawer.className = 'drawer';
    scrollContainer.append(drawer);
    renderHook(() => usePullToRefresh({ onRefresh }));

    act(() => {
      pull(drawer, { clientX: 20, clientY: 10 }, { clientX: 20, clientY: 150 });
    });

    expect(onRefresh).not.toHaveBeenCalled();
  });

});
