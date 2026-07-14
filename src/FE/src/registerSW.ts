/// <reference types="vite-plugin-pwa/client" />
import { registerSW } from 'virtual:pwa-register';

let isRefreshing = false;

const updateSW = registerSW({
  onNeedRefresh() {
    console.log('[PWA] New update available — forcing active controller swap');
    updateSW();
  },
  onOfflineReady() {
    console.log('[PWA] App is ready for offline use');
  },
  onRegisterError(error) {
    console.error('[PWA] Registration failed:', error);
  },
});

navigator.serviceWorker.addEventListener('controllerchange', () => {
  if (!isRefreshing) {
    isRefreshing = true;
    console.log('[PWA] Controller changed — reloading page');
    window.location.reload();
  }
});
