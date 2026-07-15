/// <reference types="vite-plugin-pwa/client" />
import { registerSW } from 'virtual:pwa-register';

let isRefreshing = false;

const updateSW = registerSW({
  onNeedRefresh() {
    console.log('[PWA] New update available — activating');
    updateSW();
  },
  onOfflineReady() {
    console.log('[PWA] App is ready for offline use');
  },
  onRegisterError(error) {
    console.error('[PWA] Registration failed:', error);
  },
});

navigator.serviceWorker.addEventListener('message', (event) => {
  if (event.data?.type === 'RELOAD' && !isRefreshing) {
    isRefreshing = true;
    console.log('[PWA] Received RELOAD from SW — reloading');
    window.location.reload();
  }
});

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.ready.then((reg) => {
    reg.update();
  });

  setInterval(() => {
    navigator.serviceWorker.ready.then((reg) => reg.update());
  }, 5 * 60 * 1000);
}
