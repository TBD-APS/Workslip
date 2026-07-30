/// <reference types="vite-plugin-pwa/client" />
import { registerSW } from 'virtual:pwa-register';

const UPDATE_INTERVAL_MS = 60 * 1000;
const UPDATE_RELOAD_FALLBACK_MS = 2_000;
const CONFIRMED_RELOAD_GUARD_MS = 10_000;
const CONFIRMED_RELOAD_AT_KEY = 'workslip.pwaUpdateReloadAt';
const FALLBACK_RELOAD_KEY = `workslip.pwaUpdateFallback:${__BUILD_TIME__}`;
const SKIP_WAITING_MESSAGE = { type: 'SKIP_WAITING' } as const;

const serviceWorkerSupported = 'serviceWorker' in navigator;
const hadControllerAtStartup = serviceWorkerSupported
  && Boolean(navigator.serviceWorker.controller);

let reloadRequested = false;
let reloadFallback: number | undefined;

function readSessionValue(key: string) {
  try {
    return window.sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeSessionValue(key: string, value: string) {
  try {
    window.sessionStorage.setItem(key, value);
  } catch {
    // The in-memory guard still prevents duplicate reloads in this document.
  }
}

function wasConfirmedReloadRecent() {
  const lastReloadAt = Number(readSessionValue(CONFIRMED_RELOAD_AT_KEY));
  return Number.isFinite(lastReloadAt)
    && Date.now() - lastReloadAt < CONFIRMED_RELOAD_GUARD_MS;
}

function clearReloadFallback() {
  if (reloadFallback === undefined) return;

  window.clearTimeout(reloadFallback);
  reloadFallback = undefined;
}

function reloadForUpdate() {
  if (!hadControllerAtStartup || reloadRequested || wasConfirmedReloadRecent()) return;

  reloadRequested = true;
  writeSessionValue(CONFIRMED_RELOAD_AT_KEY, Date.now().toString());
  clearReloadFallback();
  window.location.reload();
}

function reloadFromFallback() {
  reloadFallback = undefined;
  if (!hadControllerAtStartup || reloadRequested || readSessionValue(FALLBACK_RELOAD_KEY)) return;

  reloadRequested = true;
  writeSessionValue(FALLBACK_RELOAD_KEY, '1');
  window.location.reload();
}

function scheduleReloadFallback() {
  if (
    !hadControllerAtStartup
    || reloadRequested
    || reloadFallback !== undefined
    || readSessionValue(FALLBACK_RELOAD_KEY)
  ) return;

  reloadFallback = window.setTimeout(reloadFromFallback, UPDATE_RELOAD_FALLBACK_MS);
}

function activateWaitingWorker(registration: ServiceWorkerRegistration) {
  if (!hadControllerAtStartup || !registration.waiting) return false;

  registration.waiting.postMessage(SKIP_WAITING_MESSAGE);
  scheduleReloadFallback();
  return true;
}

function watchInstallingWorker(registration: ServiceWorkerRegistration) {
  if (!hadControllerAtStartup || !registration.installing) return;

  const installingWorker = registration.installing;
  const handleStateChange = () => {
    if (installingWorker.state === 'installed') {
      activateWaitingWorker(registration);
      scheduleReloadFallback();
    } else if (installingWorker.state === 'activated') {
      reloadForUpdate();
    }
  };

  installingWorker.addEventListener('statechange', handleStateChange);
  handleStateChange();
}

async function checkForServiceWorkerUpdate(
  swUrl: string,
  registration: ServiceWorkerRegistration,
) {
  if (!navigator.onLine || registration.installing) return;
  if (activateWaitingWorker(registration)) return;

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

if (serviceWorkerSupported) {
  navigator.serviceWorker.addEventListener('controllerchange', reloadForUpdate);
}

registerSW({
  immediate: true,
  onNeedReload: reloadForUpdate,
  onOfflineReady() {
    console.log('[PWA] App is ready for offline use');
  },
  onRegisteredSW(swUrl, registration) {
    if (!registration) return;

    registration.addEventListener('updatefound', () => {
      watchInstallingWorker(registration);
    });
    watchInstallingWorker(registration);
    activateWaitingWorker(registration);

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
