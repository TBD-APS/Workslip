/// <reference types="vite-plugin-pwa/client" />
import { registerSW } from 'virtual:pwa-register';

const UPDATE_INTERVAL_MS = 60 * 60 * 1000;

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
    // Offline or update check failed. The next interval/visibility change retries.
  }
}

registerSW({
  onOfflineReady() {
    console.log('[PWA] App is ready for offline use');
  },
  onRegisteredSW(swUrl, registration) {
    if (!registration) return;

    const requestUpdate = () => {
      void checkForServiceWorkerUpdate(swUrl, registration);
    };

    window.setInterval(requestUpdate, UPDATE_INTERVAL_MS);

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