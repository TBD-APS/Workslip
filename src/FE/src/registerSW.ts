/// <reference types="vite-plugin-pwa/client" />
import { registerSW } from 'virtual:pwa-register';

const updateSW = registerSW({
  onNeedRefresh() {
    console.log('[PWA] New update available — activating');
    updateSW();
  },
  onOfflineReady() {
    console.log('[PWA] App is ready for offline use');
  },
  onRegisteredSW(swUrl, registration) {
    if (!registration) return;

    // Check for updates every minute with cache-busting fetch
    setInterval(async () => {
      if (!navigator.onLine) return;

      try {
        const resp = await fetch(swUrl, {
          cache: 'no-store',
          headers: {
            'cache': 'no-store',
            'cache-control': 'no-cache',
          },
        });

        if (resp?.status === 200) {
          await registration.update();
        }
      } catch {
        // Offline or fetch failed — skip
      }
    }, 60 * 1000);

    // Also check when app becomes visible (switching back from another app)
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible' && navigator.onLine) {
        registration.update();
      }
    });
  },
  onRegisterError(error) {
    console.error('[PWA] Registration failed:', error);
  },
});
