import { useEffect, useRef, useCallback } from 'react';

interface UseInfiniteScrollOptions {
  onReachEnd: () => void;
  enabled?: boolean;
  rootMargin?: string;
  threshold?: number;
}

export function useInfiniteScroll({
  onReachEnd,
  enabled = true,
  rootMargin = '0px 0px 200px 0px',
  threshold = 0,
}: UseInfiniteScrollOptions) {
  const sentinelRef = useRef<HTMLDivElement | null>(null);
  const onReachEndRef = useRef(onReachEnd);

  useEffect(() => {
    onReachEndRef.current = onReachEnd;
  }, [onReachEnd]);

  const setSentinelRef = useCallback((node: HTMLDivElement | null) => {
    sentinelRef.current = node;
  }, []);

  useEffect(() => {
    if (!enabled || !sentinelRef.current) return;

    const observer = new IntersectionObserver(
      (entries) => {
        const entry = entries[0];
        if (entry?.isIntersecting) {
          onReachEndRef.current();
        }
      },
      { rootMargin, threshold }
    );

    const sentinel = sentinelRef.current;
    if (sentinel) {
      observer.observe(sentinel);
    }

    return () => {
      if (sentinel) {
        observer.unobserve(sentinel);
      }
    };
  }, [enabled, rootMargin, threshold]);

  return { sentinelRef: setSentinelRef };
}