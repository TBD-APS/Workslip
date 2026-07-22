import { useEffect } from 'react';

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
  // Restore on mount
  useEffect(() => {
    const saved = sessionStorage.getItem(`scroll:${key}`);
    if (saved) {
      requestAnimationFrame(() => getScrollContainer()?.scrollTo({ top: Number(saved) }));
    }
  }, [key]);

  // Save — debounced scroll listener with resize cooldown
  useEffect(() => {
    const container = getScrollContainer();
    if (!container) return;

    const myToken = Symbol();
    tokens[key] = myToken;

    let timer: ReturnType<typeof setTimeout> | undefined;
    const onScroll = () => {
      if (tokens[key] !== myToken) return;
      if (Date.now() < resizeCooldownUntil) return;
      if (timer) clearTimeout(timer);
      timer = setTimeout(() => {
        if (tokens[key] !== myToken) return;
        if (Date.now() < resizeCooldownUntil) return;
        sessionStorage.setItem(`scroll:${key}`, String(container.scrollTop));
      }, 200);
    };
    container.addEventListener('scroll', onScroll, { passive: true });
    return () => {
      if (timer) clearTimeout(timer);
      container.removeEventListener('scroll', onScroll);
      if (tokens[key] === myToken) delete tokens[key];
    };
  }, [key]);
}
