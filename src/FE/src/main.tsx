import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './registerSW'
import './index.css'
import App from './App.tsx'
import { initializeApplicationInsights, installGlobalApplicationInsightsHandlers } from './applicationInsights';

declare const __BUILD_TIME__: string;

initializeApplicationInsights();
installGlobalApplicationInsightsHandlers();

if (typeof window !== 'undefined') {
  // Vite emits this event when an already-open client references an asset that
  // disappeared after a deployment. Reload once for the current build; if the
  // same build still fails, let the normal error boundary surface the problem
  // instead of creating an infinite reload loop.
  const staleBuildRecoveryKey = `workslip.preloadRecovery:${__BUILD_TIME__}`;
  window.addEventListener('vite:preloadError', (event) => {
    try {
      if (sessionStorage.getItem(staleBuildRecoveryKey)) return;
      sessionStorage.setItem(staleBuildRecoveryKey, '1');
    } catch {
      // Without a working session guard an automatic reload could loop.
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
)
