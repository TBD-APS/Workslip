import { useEffect } from 'react';
import { recordWorkflowActiveSegment } from '../../superadmin/statisticsApi';

const FLUSH_INTERVAL_MS = 15_000;
const MIN_SEGMENT_MS = 1_000;

export function useWorkflowActiveTime(jobId: string | undefined, enabled: boolean) {
  useEffect(() => {
    if (!jobId || !enabled) return undefined;

    let activeSince: number | null = null;

    const isActive = () => document.visibilityState === 'visible' && document.hasFocus();

    const start = () => {
      if (activeSince === null && isActive()) {
        activeSince = performance.now();
      }
    };

    const flush = () => {
      if (activeSince === null) return;
      const elapsedMs = performance.now() - activeSince;
      activeSince = null;

      if (elapsedMs >= MIN_SEGMENT_MS) {
        void recordWorkflowActiveSegment(jobId, elapsedMs / 1000);
      }
    };

    const restart = () => {
      flush();
      start();
    };

    const handleVisibility = () => {
      if (document.visibilityState === 'hidden') {
        flush();
      } else {
        start();
      }
    };

    const interval = window.setInterval(restart, FLUSH_INTERVAL_MS);
    window.addEventListener('focus', start);
    window.addEventListener('blur', flush);
    window.addEventListener('pagehide', flush);
    document.addEventListener('visibilitychange', handleVisibility);
    start();

    return () => {
      window.clearInterval(interval);
      window.removeEventListener('focus', start);
      window.removeEventListener('blur', flush);
      window.removeEventListener('pagehide', flush);
      document.removeEventListener('visibilitychange', handleVisibility);
      flush();
    };
  }, [enabled, jobId]);
}