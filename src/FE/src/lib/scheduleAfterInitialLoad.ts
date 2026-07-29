export function scheduleAfterInitialLoad(task: () => void, timeoutMs = 2_000): () => void {
  let idleHandle: number | undefined;
  let fallbackHandle: number | undefined;

  const schedule = () => {
    if ('requestIdleCallback' in window) {
      idleHandle = window.requestIdleCallback(task, { timeout: timeoutMs });
      return;
    }

    fallbackHandle = window.setTimeout(task, 0);
  };

  if (document.readyState === 'complete') {
    schedule();
  } else {
    window.addEventListener('load', schedule, { once: true });
  }

  return () => {
    window.removeEventListener('load', schedule);
    if (idleHandle !== undefined && 'cancelIdleCallback' in window) {
      window.cancelIdleCallback(idleHandle);
    }
    if (fallbackHandle !== undefined) {
      window.clearTimeout(fallbackHandle);
    }
  };
}
