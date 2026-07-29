type IdleCapableWindow = Window & {
  requestIdleCallback?: (callback: IdleRequestCallback, options?: IdleRequestOptions) => number;
  cancelIdleCallback?: (handle: number) => void;
};

export function scheduleAfterInitialLoad(task: () => void, timeoutMs = 2_000): () => void {
  const idleWindow = window as IdleCapableWindow;
  let idleHandle: number | undefined;
  let fallbackHandle: number | undefined;

  const schedule = () => {
    if (idleWindow.requestIdleCallback) {
      idleHandle = idleWindow.requestIdleCallback(task, { timeout: timeoutMs });
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
    if (idleHandle !== undefined) {
      idleWindow.cancelIdleCallback?.(idleHandle);
    }
    if (fallbackHandle !== undefined) {
      window.clearTimeout(fallbackHandle);
    }
  };
}

export function scheduleDeferredTelemetry(task: () => void, delayMs = 10_000): () => void {
  let started = false;
  let delayHandle: number | undefined;
  let cancelScheduledTask: (() => void) | undefined;

  function removeTriggers() {
    window.removeEventListener('pointerdown', start);
    window.removeEventListener('keydown', start);
    if (delayHandle !== undefined) window.clearTimeout(delayHandle);
  }

  function start() {
    if (started) return;
    started = true;
    removeTriggers();
    cancelScheduledTask = scheduleAfterInitialLoad(task);
  }

  delayHandle = window.setTimeout(start, delayMs);
  window.addEventListener('pointerdown', start, { once: true, passive: true });
  window.addEventListener('keydown', start, { once: true });

  return () => {
    removeTriggers();
    cancelScheduledTask?.();
  };
}
