import { useEffect } from 'react';
import { recordWorkflowActiveSegment } from '../../superadmin/statisticsApi';

const FLUSH_INTERVAL_MS = 15_000;
const IDLE_TIMEOUT_MS = 30_000;
const MIN_SEGMENT_MS = 1_000;
const ACTIVITY_EVENTS: Array<keyof WindowEventMap> = ['pointerdown', 'keydown', 'touchstart', 'wheel'];

export function useWorkflowActiveTime(jobId: string | undefined, enabled: boolean) {
  useEffect(() => {
    if (!jobId || !enabled) return undefined;

    let activeSince: number | null = null;
    let lastActivityAt = performance.now();

    const isPageActive = () => document.visibilityState === 'visible' && document.hasFocus();

    const flush = (until = performance.now()) => {
      if (activeSince === null) return;

      const activeUntil = Math.min(until, lastActivityAt + IDLE_TIMEOUT_MS);
      const elapsedMs = Math.max(0, activeUntil - activeSince);
      activeSince = null;

      if (elapsedMs >= MIN_SEGMENT_MS) {
        void recordWorkflowActiveSegment(jobId, elapsedMs / 1000);
      }
    };

    const markActivity = () => {
      if (!isPageActive()) return;

      const now = performance.now();
      if (now - lastActivityAt >= IDLE_TIMEOUT_MS) {
        flush(lastActivityAt + IDLE_TIMEOUT_MS);
        activeSince = now;
      } else if (activeSince === null) {
        activeSince = now;
      }
      lastActivityAt = now;
    };

    const handleFocus = () => {
      lastActivityAt = performance.now();
      activeSince = lastActivityAt;
    };

    const handleBlur = () => {
      flush();
    };

    const handleVisibility = () => {
      if (document.visibilityState === 'hidden') {
        flush();
      } else if (document.hasFocus()) {
        handleFocus();
      }
    };

    const sample = () => {
      const now = performance.now();
      if (!isPageActive()) {
        flush(now);
        return;
      }

      if (now - lastActivityAt >= IDLE_TIMEOUT_MS) {
        flush(lastActivityAt + IDLE_TIMEOUT_MS);
        return;
      }

      flush(now);
      activeSince = now;
    };

    const interval = window.setInterval(sample, FLUSH_INTERVAL_MS);
    window.addEventListener('focus', handleFocus);
    window.addEventListener('blur', handleBlur);
    window.addEventListener('pagehide', handleBlur);
    ACTIVITY_EVENTS.forEach((eventName) => window.addEventListener(eventName, markActivity, { passive: true }));
    document.addEventListener('visibilitychange', handleVisibility);
    handleFocus();

    return () => {
      window.clearInterval(interval);
      window.removeEventListener('focus', handleFocus);
      window.removeEventListener('blur', handleBlur);
      window.removeEventListener('pagehide', handleBlur);
      ACTIVITY_EVENTS.forEach((eventName) => window.removeEventListener(eventName, markActivity));
      document.removeEventListener('visibilitychange', handleVisibility);
      flush();
    };
  }, [enabled, jobId]);
}