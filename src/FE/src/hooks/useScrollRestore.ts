import { useEffect, useLayoutEffect, useRef } from 'react';
import { useAppScrollRestoreKey } from './useAppRouteScroll';

function getScrollContainer(): HTMLElement | null {
  return document.querySelector('.app-shell');
}

// Global scroll write tokens — one per key.
// Only the component holding the current token may write to sessionStorage.
const tokens: Record<string, symbol> = {};

// Resize cooldown: shared with usePaginatedList
let resizeCooldownUntil = 0;
window.addEventListener('resize', () => {
  resizeCooldownUntil = Date.now() + 500;
}, { passive: true });

/**
 * Saves and restores `.app-shell` scroll position across route transitions.
 * Pass a unique key (e.g. the route path or entity id).
 */
export function useScrollRestore(key: string) {
  const restoreKey = useAppScrollRestoreKey();
  const restorePendingRef = useRef(false);

  useLayoutEffect(() => {
    restorePendingRef.current = Boolean(restoreKey);
  }, [restoreKey]);

  // Restore after mount. Scroll writes stay suppressed until this has either
  // consumed the saved position or established that there is nothing to use.
  useEffect(() => {
    if (!restoreKey) return;

    const saved = sessionStorage.getItem(`scroll:${key}`);
    if (!saved) {
      restorePendingRef.current = false;
      return;
    }

    const scrollTop = Number(saved);
    if (!Number.isFinite(scrollTop) || scrollTop < 0) {
      restorePendingRef.current = false;
      return;
    }

    const frame = requestAnimationFrame(() => {
      getScrollContainer()?.scrollTo({ top: scrollTop });
      restorePendingRef.current = false;
    });
    return () => cancelAnimationFrame(frame);
  }, [key, restoreKey]);

  // Save — debounced scroll listener with resize cooldown
  useLayoutEffect(() => {
    const container = getScrollContainer();
    if (!container) return;

    const myToken = Symbol();
    tokens[key] = myToken;

    let timer: ReturnType<typeof setTimeout> | undefined;
    let latestScrollTop = container.scrollTop;
    const onScroll = () => {
      latestScrollTop = container.scrollTop;
      if (restorePendingRef.current) return;
      if (tokens[key] !== myToken) return;
      if (Date.now() < resizeCooldownUntil) return;
      if (timer) clearTimeout(timer);
      timer = setTimeout(() => {
        if (restorePendingRef.current) return;
        if (tokens[key] !== myToken) return;
        if (Date.now() < resizeCooldownUntil) return;
        sessionStorage.setItem(`scroll:${key}`, String(latestScrollTop));
      }, 200);
    };
    container.addEventListener('scroll', onScroll, { passive: true });
    return () => {
      if (timer) clearTimeout(timer);
      container.removeEventListener('scroll', onScroll);
      if (tokens[key] === myToken) {
        if (!restorePendingRef.current) {
          sessionStorage.setItem(`scroll:${key}`, String(latestScrollTop));
        }
        delete tokens[key];
      }
    };
  }, [key]);
}
