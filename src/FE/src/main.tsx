import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './fonts.css';
import './index.css';
import App from './App.tsx';
import { initializeApplicationInsights, installGlobalApplicationInsightsHandlers } from './applicationInsights';
import { scheduleAfterInitialLoad } from './lib/scheduleAfterInitialLoad';

if (typeof window !== 'undefined') {
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
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);

scheduleAfterInitialLoad(() => {
  void import('./registerSW');
  void initializeApplicationInsights().then(installGlobalApplicationInsightsHandlers);
});
