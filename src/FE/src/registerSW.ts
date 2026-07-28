/// <reference types="vite-plugin-pwa/client" />
import { registerSW } from 'virtual:pwa-register';

const UPDATE_INTERVAL_MS = 60 * 1000;

async function checkForServiceWorkerUpdate(
  swUrl: string,
  registration: ServiceWorkerRegistration,
) {
  if (!navigator.onLine || registration.installing || registration.waiting) return;

  try {
    const response = await fetch(swUrl, {
      cache: 'no-store',
      headers: {
        'cache': 'no-store',
        'cache-control': 'no-cache',
      },
    });

    if (response.status === 200) {
      await registration.update();
    }
  } catch {
    // Startup, visibility, online and interval checks all retry this path.
  }
}

registerSW({
  onOfflineReady() {
    console.log('[PWA] App is ready for offline use');
  },
  onRegisteredSW(swUrl, registration) {
    if (!registration) return;

    let updateCheck: Promise<void> | null = null;
    const requestUpdate = () => {
      if (updateCheck) return;

      updateCheck = checkForServiceWorkerUpdate(swUrl, registration)
        .finally(() => {
          updateCheck = null;
        });
    };

    // Discover a deployment immediately when the app starts, returns to the
    // foreground or regains connectivity, and at most one minute afterwards.
    requestUpdate();
    window.setInterval(requestUpdate, UPDATE_INTERVAL_MS);
    window.addEventListener('online', requestUpdate);

    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') {
        requestUpdate();
      }
    });
  },
  onRegisterError(error) {
    console.error('[PWA] Registration failed:', error);
  },
});