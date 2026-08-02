import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './base.css';
import App from './App.tsx';
import { initializeApplicationInsights, installGlobalApplicationInsightsHandlers } from './applicationInsights';
import { scheduleAfterInitialLoad, scheduleDeferredTelemetry } from './lib/scheduleAfterInitialLoad';
import { queryClient } from './lib/react-query';
import { installNotificationNavigationHandler, installNotificationReceivedInvalidator } from './pwa/notificationNavigationClient';
import { router } from './routes';

if (typeof window !== 'undefined') {
  // Install lightweight handlers before React renders. Errors are sanitized and
  // buffered until the deferred Application Insights client is ready.
  installGlobalApplicationInsightsHandlers();

  // Vite emits this event when an already-open client references a hashed lazy
  // chunk that disappeared after deployment. Reload once for this build; if it
  // still fails, the normal error boundary handles it without a reload loop.
  const staleBuildRecoveryKey = `workslip.preloadRecovery:${__BUILD_TIME__}`;
  window.addEventListener('vite:preloadError', (event) => {
    try {
      if (sessionStorage.getItem(staleBuildRecoveryKey)) return;
      sessionStorage.setItem(staleBuildRecoveryKey, '1');
    } catch {
      // Without a reliable guard an automatic reload could loop indefinitely.
      return;
    }

    event.preventDefault();
    window.location.reload();
  });

  const originalFocus = HTMLInputElement.prototype.focus;

  HTMLInputElement.prototype.focus = function (options?: FocusOptions) {
    // Hvis der ikke eksplicit er angivet options, tvinger vi preventScroll: true
    const newOptions: FocusOptions = {
      preventScroll: true,
      ...options,
    };
    originalFocus.call(this, newOptions);
  };

  if ('serviceWorker' in navigator) {
    installNotificationNavigationHandler(
      navigator.serviceWorker,
      window.location.origin,
      (target) => router.navigate(target),
    );
    installNotificationReceivedInvalidator(
      navigator.serviceWorker,
      () => queryClient.invalidateQueries({ queryKey: ['/api/jobs'] }),
    );
  }
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);

scheduleAfterInitialLoad(() => {
  void import('./registerSW');
});

scheduleDeferredTelemetry(() => {
  void initializeApplicationInsights();
});
