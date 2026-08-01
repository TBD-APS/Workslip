import { useEffect, useRef, useState } from 'react';

const DEFAULT_THRESHOLD = 64;
const MAX_PULL_DISTANCE = 88;
const DIRECTION_LOCK_DISTANCE = 12;
const PULL_RESISTANCE = 0.5;

type UsePullToRefreshOptions = {
  onRefresh: () => Promise<unknown> | unknown;
  enabled?: boolean;
  threshold?: number;
  getScrollContainer?: () => HTMLElement | null;
};

type PullToRefreshState = {
  pullDistance: number;
  isRefreshing: boolean;
  willRefresh: boolean;
};

const getDefaultScrollContainer = () =>
  document.querySelector<HTMLElement>('.app-shell');

export function usePullToRefresh({
  onRefresh,
  enabled = true,
  threshold = DEFAULT_THRESHOLD,
  getScrollContainer = getDefaultScrollContainer,
}: UsePullToRefreshOptions): PullToRefreshState {
  const [pullDistance, setPullDistance] = useState(0);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const onRefreshRef = useRef(onRefresh);
  const isRefreshingRef = useRef(false);

  useEffect(() => {
    onRefreshRef.current = onRefresh;
  }, [onRefresh]);

  useEffect(() => {
    if (!enabled) return;

    const scrollContainer = getScrollContainer();
    if (!scrollContainer) return;

    let startX = 0;
    let startY = 0;
    let currentPullDistance = 0;
    let isTracking = false;

    const resetGesture = () => {
      isTracking = false;
      currentPullDistance = 0;
      setPullDistance(0);
    };

    const handleTouchStart = (event: TouchEvent) => {
      if (event.touches.length !== 1) {
        resetGesture();
        return;
      }

      const target = event.target;
      if (
        isRefreshingRef.current
        || scrollContainer.scrollTop > 0
        || (target instanceof Element && target.closest('.drawer, .create-sheet'))
      ) {
        return;
      }

      const touch = event.touches[0];
      startX = touch.clientX;
      startY = touch.clientY;
      currentPullDistance = 0;
      isTracking = true;
      setPullDistance(0);
    };

    const handleTouchMove = (event: TouchEvent) => {
      if (!isTracking) return;
      if (event.touches.length !== 1) {
        resetGesture();
        return;
      }

      const touch = event.touches[0];
      const deltaX = touch.clientX - startX;
      const deltaY = touch.clientY - startY;

      if (Math.max(Math.abs(deltaX), Math.abs(deltaY)) < DIRECTION_LOCK_DISTANCE) return;

      if (Math.abs(deltaX) > Math.abs(deltaY) || deltaY <= 0 || scrollContainer.scrollTop > 0) {
        resetGesture();
        return;
      }

      if (event.cancelable) event.preventDefault();
      currentPullDistance = Math.min(deltaY * PULL_RESISTANCE, MAX_PULL_DISTANCE);
      setPullDistance(currentPullDistance);
    };

    const handleTouchEnd = (event: TouchEvent) => {
      if (!isTracking) return;
      if (event.touches.length > 0) {
        resetGesture();
        return;
      }

      const shouldRefresh = currentPullDistance >= threshold && !isRefreshingRef.current;
      resetGesture();
      if (!shouldRefresh) return;

      isRefreshingRef.current = true;
      setIsRefreshing(true);

      void Promise.resolve()
        .then(() => onRefreshRef.current())
        .catch(() => undefined)
        .finally(() => {
          isRefreshingRef.current = false;
          setIsRefreshing(false);
        });
    };

    scrollContainer.addEventListener('touchstart', handleTouchStart, { passive: true });
    scrollContainer.addEventListener('touchmove', handleTouchMove, { passive: false });
    scrollContainer.addEventListener('touchend', handleTouchEnd, { passive: true });
    scrollContainer.addEventListener('touchcancel', resetGesture, { passive: true });

    return () => {
      scrollContainer.removeEventListener('touchstart', handleTouchStart);
      scrollContainer.removeEventListener('touchmove', handleTouchMove);
      scrollContainer.removeEventListener('touchend', handleTouchEnd);
      scrollContainer.removeEventListener('touchcancel', resetGesture);
    };
  }, [enabled, getScrollContainer, threshold]);

  return {
    pullDistance: enabled ? pullDistance : 0,
    isRefreshing,
    willRefresh: enabled && pullDistance >= threshold,
  };
}
